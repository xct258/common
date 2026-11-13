#!/bin/bash
# ==========================================
# 机械硬盘自动休眠监控脚本
# 监控机械硬盘 IO 活动，空闲超时后自动休眠
# ==========================================

# ====== 用户配置区域 ======
# 排除不控制休眠的 ZFS 池（这些盘仍会监控温度/健康、显示在通知中，但不会自动休眠）
EXCLUDE_SLEEP_POOLS=(
  backup-1
)
# 排除不监控健康的硬盘或池（完全忽略，不出现在通知和健康检查中）
EXCLUDE_HEALTH_DISKS=(
  boot-pool
)
# 无活动多少秒后休眠（默认 1800 秒 = 30 分钟）
IDLE_TIMEOUT=60
# iostat 检测间隔（秒）
CHECK_INTERVAL=5
# 休眠命令（默认 hdparm -y）
SLEEP_CMD="hdparm -y"
# 硬盘温度/健康检查间隔（秒），需大于 IDLE_TIMEOUT 以保证硬盘能先休眠
TEMP_CHECK_INTERVAL=600
# 池剩余空间低于多少 GB 时告警
POOL_FREE_WARN_GB=50
# 单独设置某些池的剩余空间告警阈值（GB），未设置的池使用 POOL_FREE_WARN_GB
declare -A POOL_FREE_WARN_GB_MAP=(
  [boot-pool]=6
)
# HDD/SSD 温度达到多少 °C 时告警
HDD_TEMP_WARN=53
SSD_TEMP_WARN=60
# 状态变化推送 URL（留空=不推送）
WEBHOOK_URL="https://push.o1-1.xct258.top/api/push"
WEBHOOK_SERVER="pve-1-nas-1"
WEBHOOK_TOPIC="监控/硬盘状态"
# ==========================

set -euo pipefail

cleanup() {
    echo "监控已停止。"
    exit 0
}
trap cleanup SIGINT SIGTERM

# 检查依赖
MISSING=""
for cmd in iostat smartctl hdparm; do
    command -v "$cmd" &>/dev/null || MISSING="$MISSING $cmd"
done
if [[ -n "$MISSING" ]]; then
    echo "错误: 未找到以下命令，请先安装: $MISSING"
    echo "  Debian/Ubuntu: sudo apt install sysstat smartmontools hdparm"
    exit 1
fi

# ====== 推送通知 ======
notify() {
    [[ -z "$WEBHOOK_URL" ]] && return
    local event=$1
    local ts sc_ret
    ts=$(date '+%m-%d %H:%M:%S')
    refresh_pool_space_map
    local msg=""
    declare -A shown_pools=()
    for d in "${WATCH_DISKS[@]}"; do
        [[ -n "$msg" ]] && msg+=$'\n'
        local base_pool="${ALL_BASE_POOLS[$d]}"
        local free="${POOL_SPACE_MAP[$base_pool]:--}"
        [[ "$base_pool" != "未分配池" ]] && shown_pools["$base_pool"]=true
        local sleeping=false
        if [[ " ${MONITOR_DISKS[*]} " == *" $d "* ]]; then
            [[ "${DISK_SLEPT["$d"]}" == "true" ]] && sleeping=true
        else
            sc_ret=0
            smartctl -i -n standby "/dev/$d" &>/dev/null || sc_ret=$?
            [[ $sc_ret -eq 2 ]] && sleeping=true
        fi
        if $sleeping; then
            msg+="💤 ${ALL_POOLS[$d]}"
            msg+=$'\n'"  温度: -"
            msg+=$'\n'"  健康: -"
            msg+=$'\n'"  剩余: ${free}$(free_warn "$base_pool" "$free")"
            msg+=$'\n'"  更新: ${ts}"
        else
            local t="${DISK_TEMP["$d"]:-}" h="${DISK_HEALTH["$d"]:-}"
            if [[ -z "$t" || -z "$h" ]]; then
                read_health "$d"
                t="${DISK_TEMP["$d"]}"
                h="${DISK_HEALTH["$d"]}"
            fi
            local icon="🚀"
            [[ "${DISK_ROTA["$d"]}" == "0" ]] && icon="⚡"
            local ut="${DISK_UPDATED["$d"]:-$ts}"
            msg+="${icon} ${ALL_POOLS[$d]}"
            msg+=$'\n'"  温度: ${t}$(temp_warn "$d" "$t")"
            msg+=$'\n'"  健康: ${h}"
            msg+=$'\n'"  剩余: ${free}$(free_warn "$base_pool" "$free")"
            msg+=$'\n'"  更新: ${ut}"
        fi
    done
    for pool in "${!POOL_SPACE_MAP[@]}"; do
        [[ -n "${shown_pools[$pool]:-}" ]] && continue
        [[ -n "$msg" ]] && msg+=$'\n'
        msg+="📦 ${pool}"
        msg+=$'\n'"  剩余: ${POOL_SPACE_MAP[$pool]}$(free_warn "$pool" "${POOL_SPACE_MAP[$pool]}")"
        msg+=$'\n'"  更新: ${ts}"
    done
    local json
    json=$(printf '{"server_name":"%s","topic":"%s","message":"%s","push_to_mqtt":false,"mode":"overwrite"}' \
        "$WEBHOOK_SERVER" "$WEBHOOK_TOPIC" "$msg")
    json="${json//$'\n'/\\n}"
    curl -s -X POST "$WEBHOOK_URL" \
        -H "Content-Type: application/json" \
        -d "$json" &>/dev/null || true
}

echo "========================================"
echo " 硬盘状态监控脚本"
echo "========================================"
echo ""

# ====== 硬盘发现与池映射 ======
echo ">> 正在扫描硬盘..."

declare -a HDD_LIST
declare -A HDD_POOLS
declare -A TEMP_BASE_POOLS
declare -A POOL_COUNTS

raw_output=$(lsblk -d -n -o NAME,TYPE,ROTA)

while read -r name type rota; do
    [[ "$type" != "disk" ]] || [[ "$rota" != "1" ]] && continue
    HDD_LIST+=("$name")
    pool_name=$(lsblk -n -o FSTYPE,LABEL "/dev/$name" 2>/dev/null | awk '$1=="zfs_member" {print $2}' | head -n 1)
    [[ -z "$pool_name" ]] && pool_name="未分配池"
    TEMP_BASE_POOLS["$name"]="$pool_name"
    POOL_COUNTS["$pool_name"]=$(( ${POOL_COUNTS["$pool_name"]-0} + 1 ))
done <<< "$raw_output"

if [[ ${#HDD_LIST[@]} -eq 0 ]]; then
    echo "未找到机械硬盘，仅监控温度/健康。"
fi

for disk in "${HDD_LIST[@]}"; do
    base_pool="${TEMP_BASE_POOLS[$disk]}"
    final_pool_name="$base_pool"
    if [[ "${POOL_COUNTS[$base_pool]}" -gt 1 ]] && [[ "$base_pool" != "未分配池" ]]; then
        final_pool_name="${base_pool}_${disk}"
    fi
    HDD_POOLS["$disk"]="$final_pool_name"
done

# ====== 全部硬盘发现（含 SSD/NVMe，用于温度/健康监控）======
declare -a ALL_DISKS
declare -A DISK_ROTA
declare -A ALL_BASE_POOLS
declare -A ALL_POOL_COUNTS

all_raw=$(lsblk -d -n -o NAME,TYPE,ROTA)
while read -r name type rota; do
    [[ "$type" != "disk" ]] || [[ "$name" == zd* ]] && continue
    ALL_DISKS+=("$name")
    DISK_ROTA["$name"]="$rota"
    pool_name=$(lsblk -n -o FSTYPE,LABEL "/dev/$name" 2>/dev/null | awk '$1=="zfs_member" {print $2}' | head -n 1)
    [[ -z "$pool_name" ]] && pool_name="未分配池"
    ALL_BASE_POOLS["$name"]="$pool_name"
    ALL_POOL_COUNTS["$pool_name"]=$(( ${ALL_POOL_COUNTS["$pool_name"]-0} + 1 ))
done <<< "$all_raw"

declare -A ALL_POOLS
for disk in "${ALL_DISKS[@]}"; do
    base="${ALL_BASE_POOLS[$disk]}"
    final="$base"
    if [[ "${ALL_POOL_COUNTS[$base]}" -gt 1 ]] && [[ "$base" != "未分配池" ]]; then
        final="${base}_${disk}"
    fi
    ALL_POOLS["$disk"]="$final"
done

# ====== 池空间映射 ======
declare -A POOL_SPACE_MAP
refresh_pool_space_map() {
    POOL_SPACE_MAP=()
    local line pname pfree
    while IFS= read -r line; do
        [[ -z "$line" ]] && continue
        pname="${line%%$'\t'*}"
        pfree="${line#*$'\t'}"
        POOL_SPACE_MAP["$pname"]="$pfree"
    done <<< "$(zpool list -H -o name,free 2>/dev/null || true)"
}
refresh_pool_space_map

free_warn() {
    local pool=$1
    local value=$2
    local warn="${POOL_FREE_WARN_GB_MAP[$pool]:-$POOL_FREE_WARN_GB}"
    awk -v value="$value" -v warn="$warn" '
        BEGIN {
            if (value == "" || value == "-") exit 0
            number = value + 0
            unit = substr(value, length(value), 1)
            if (unit == "K") number = number / 1024 / 1024
            else if (unit == "M") number = number / 1024
            else if (unit == "T") number = number * 1024
            else if (unit == "P") number = number * 1024 * 1024
            if (number < warn) printf " ⚠️"
        }
    '
}

temp_warn() {
    local disk=$1
    local temp=$2
    [[ "$temp" =~ ^[0-9]+$ ]] || return 0
    local warn=$HDD_TEMP_WARN
    [[ "${DISK_ROTA["$disk"]}" == "0" ]] && warn=$SSD_TEMP_WARN
    [[ "$temp" -ge "$warn" ]] && printf " ⚠️"
    return 0
}

# ====== 过滤排除（温度/健康监控用）======
WATCH_DISKS=()
for disk in "${ALL_DISKS[@]}"; do
    excluded=false
    for eh in "${EXCLUDE_HEALTH_DISKS[@]}"; do
        pool="${ALL_POOLS[$disk]}"
        [[ "$disk" == "$eh" || "$pool" == "$eh" || "$pool" == "${eh}_${disk}" ]] && { excluded=true; break; }
    done
    if $excluded; then
        echo "跳过健康监控: /dev/$disk (${ALL_POOLS[$disk]})"
    else
        WATCH_DISKS+=("$disk")
    fi
done

# ====== 过滤排除池（休眠监控用）======
MONITOR_DISKS=()
for disk in "${HDD_LIST[@]}"; do
    pool="${HDD_POOLS[$disk]}"
    excluded=false
    for ep in "${EXCLUDE_SLEEP_POOLS[@]}"; do
        if [[ "$pool" == "$ep" || "$pool" == "${ep}_${disk}" ]]; then
            excluded=true
            break
        fi
    done
    if $excluded; then
        echo "跳过: /dev/$disk ($pool) - 已排除"
    else
        MONITOR_DISKS+=("$disk")
        echo "监控: /dev/$disk ($pool)"
    fi
done

if [[ ${#MONITOR_DISKS[@]} -eq 0 ]] && [[ ${#WATCH_DISKS[@]} -eq 0 ]]; then
    echo "没有需要监控的硬盘，脚本退出。"
    exit 0
fi

# ====== 初始化状态跟踪 ======
declare -A IDLE_COUNT
declare -A DISK_SLEPT
declare -A DISK_TEMP
declare -A DISK_HEALTH
declare -A DISK_UPDATED
declare -A LAST_IO
TEMP_ELAPSED=0
for disk in "${MONITOR_DISKS[@]}"; do
    IDLE_COUNT["$disk"]=0
    DISK_SLEPT["$disk"]=false
    LAST_IO["$disk"]=false
done
for disk in "${WATCH_DISKS[@]}"; do
    [[ -v DISK_SLEPT["$disk"] ]] && continue
    DISK_SLEPT["$disk"]=false
    DISK_TEMP["$disk"]=""
    DISK_HEALTH["$disk"]=""
done

read_health() {
    local disk=$1
    local t h
    t=$(smartctl -A "/dev/$disk" 2>/dev/null | awk '/Temperature_Celsius/ {print $10; exit}') || true
    [[ -z "$t" ]] && t=$(smartctl -A "/dev/$disk" 2>/dev/null | awk -F'[:.]' '/Current Drive Temperature/ {gsub(/[^0-9]/,"",$2); print $2; exit}') || true
    [[ -z "$t" ]] && t="N/A"
    DISK_TEMP["$disk"]="$t"
    h=$(smartctl -H "/dev/$disk" 2>/dev/null | grep -oP '(PASSED|FAILED|Unknown)') || h="N/A"
    DISK_HEALTH["$disk"]="$h"
    DISK_UPDATED["$disk"]=$(date '+%m-%d %H:%M:%S')
}

sleep_disk() {
    local disk=$1
    local now
    now=$(date '+%Y-%m-%d %H:%M:%S')
    echo ""
    echo "===== $now ====="
    echo ">> ${HDD_POOLS[$disk]} (/dev/$disk) 已空闲 ${IDLE_TIMEOUT} 秒，执行休眠..."
    sc_ret=0
    smartctl -i -n standby "/dev/$disk" &>/dev/null || sc_ret=$?
    if [[ $sc_ret -eq 2 ]]; then
        echo "  💤 ${HDD_POOLS[$disk]}  已在休眠"
        DISK_SLEPT["$disk"]=true
        return
    fi
    if $SLEEP_CMD "/dev/$disk" &>/dev/null; then
        echo "  💤 休眠命令已发送"
        DISK_SLEPT["$disk"]=true
        notify "[休眠] ${HDD_POOLS[$disk]}"
    else
        echo "  FAIL 休眠命令失败"
    fi
}

# ====== 初始化状态显示（一次性 smartctl，不走内存默认值） ======
echo ""
init_now=$(date '+%Y-%m-%d %H:%M:%S')
echo "$init_now"
for disk in "${MONITOR_DISKS[@]}"; do
    sc_ret=0
    smartctl -i -n standby "/dev/$disk" &>/dev/null || sc_ret=$?
    if [[ $sc_ret -eq 2 ]]; then
        echo "  💤 ${HDD_POOLS[$disk]}"
        DISK_SLEPT["$disk"]=true
    else
        echo "  🚀 ${HDD_POOLS[$disk]}  闲置0s / 剩余${IDLE_TIMEOUT}s"
        DISK_SLEPT["$disk"]=false
    fi
done
for disk in "${WATCH_DISKS[@]}"; do
    [[ " ${MONITOR_DISKS[*]} " == *" $disk "* ]] && continue
    sc_ret=0
    smartctl -i -n standby "/dev/$disk" &>/dev/null || sc_ret=$?
    if [[ $sc_ret -eq 2 ]]; then
        echo "  💤 ${ALL_POOLS[$disk]}"
        DISK_SLEPT["$disk"]=true
    else
        read_health "$disk"
        icon="🚀"
        [[ "${DISK_ROTA["$disk"]}" == "0" ]] && icon="⚡"
        echo "  ${icon} ${ALL_POOLS[$disk]}  ${DISK_TEMP[$disk]}°C$(temp_warn "$disk" "${DISK_TEMP[$disk]}")  健康: ${DISK_HEALTH[$disk]}"
    fi
done

# 启动时推送一次全量状态
notify "[启动] 硬盘状态"

# ====== 主监控循环 ======
echo ""
echo "========================================"
echo "开始监控 | 间隔 ${CHECK_INTERVAL}s | 超时 ${IDLE_TIMEOUT}s | SSD 温度 ${TEMP_CHECK_INTERVAL}s"
echo "按 Ctrl+C 停止"
echo "========================================"

while true; do
if [[ ${#MONITOR_DISKS[@]} -gt 0 ]]; then
    # iostat 产生 2 份报告（since-boot + 间隔），取最后 N 行设备数据
    output=$(iostat -x -d "${MONITOR_DISKS[@]}" "$CHECK_INTERVAL" 2 2>/dev/null) || true
    device_lines=$(echo "$output" | grep -E '^[sh]d[a-z]' | tail -n ${#MONITOR_DISKS[@]})

    while read -r line; do
        read -r -a cols <<< "$line"
        [[ ${#cols[@]} -eq 0 ]] && continue
        dev="${cols[0]}"
        util="${cols[-1]}"

        if [[ " ${MONITOR_DISKS[*]} " != *" $dev "* ]]; then
            continue
        fi

        if [[ "$util" == "0.00" ]]; then
            LAST_IO["$dev"]=false
            [[ "${DISK_SLEPT["$dev"]}" == "true" ]] && continue
            IDLE_COUNT["$dev"]=$((IDLE_COUNT["$dev"] + 1))
        else
            LAST_IO["$dev"]=true
            if [[ "${DISK_SLEPT["$dev"]}" == "true" ]]; then
                read_health "$dev"
                echo "$(date '+%Y-%m-%d %H:%M:%S') ${ALL_POOLS[$dev]}  被 IO 活动唤醒"
                DISK_SLEPT["$dev"]=false
                notify "[IO唤醒] ${ALL_POOLS[$dev]}"
            fi
            IDLE_COUNT["$dev"]=0
        fi

        elapsed=$(( IDLE_COUNT["$dev"] * CHECK_INTERVAL ))
        if [[ "$elapsed" -ge "$IDLE_TIMEOUT" ]]; then
            sleep_disk "$dev"
        fi
    done <<< "$device_lines"
else
    sleep "$CHECK_INTERVAL"
fi

    now=$(date '+%Y-%m-%d %H:%M:%S')
    echo "$now"
    for disk in "${MONITOR_DISKS[@]}"; do
        sc_ret=0
        smartctl -i -n standby "/dev/$disk" &>/dev/null || sc_ret=$?
        if [[ $sc_ret -eq 2 ]]; then
            echo "  💤 ${HDD_POOLS[$disk]}"
            DISK_SLEPT["$disk"]=true
        else
            if [[ "${DISK_SLEPT["$disk"]}" == "true" ]]; then
                read_health "$disk"
                echo "  🚀 ${HDD_POOLS[$disk]}  被唤醒"
                DISK_SLEPT["$disk"]=false
                IDLE_COUNT["$disk"]=0
                notify "[唤醒] ${HDD_POOLS[$disk]}"
            fi
            elapsed=$(( IDLE_COUNT["$disk"] * CHECK_INTERVAL ))
            remain=$(( IDLE_TIMEOUT - elapsed ))
            [[ $remain -lt 0 ]] && remain=0
            echo "  🚀 ${HDD_POOLS[$disk]}  闲置${elapsed}s / 剩余${remain}s"
        fi
    done

    # 温度和健康检查（仅唤醒盘）
    TEMP_ELAPSED=$((TEMP_ELAPSED + CHECK_INTERVAL))
    if [[ $TEMP_ELAPSED -ge $TEMP_CHECK_INTERVAL ]]; then
        TEMP_ELAPSED=0
        refresh_pool_space_map
        declare -A LOGGED_POOLS=()
        echo "  --- 硬盘温度/健康 ---"
        for disk in "${WATCH_DISKS[@]}"; do
            [[ "${DISK_SLEPT["$disk"]}" == "true" ]] && continue
            read_health "$disk"
            icon="🚀"
            [[ "${DISK_ROTA["$disk"]}" == "0" ]] && icon="⚡"
            base_pool="${ALL_BASE_POOLS[$disk]}"
            [[ "$base_pool" != "未分配池" ]] && LOGGED_POOLS["$base_pool"]=true
            free="${POOL_SPACE_MAP[$base_pool]:--}"
            echo "  ${icon} ${ALL_POOLS[$disk]}  ${DISK_TEMP[$disk]}°C$(temp_warn "$disk" "${DISK_TEMP[$disk]}")  健康: ${DISK_HEALTH[$disk]}  剩余: ${free}$(free_warn "$base_pool" "$free")"
        done
        for pool in "${!POOL_SPACE_MAP[@]}"; do
            [[ -n "${LOGGED_POOLS[$pool]:-}" ]] && continue
            echo "  📦 ${pool}  剩余: ${POOL_SPACE_MAP[$pool]}$(free_warn "$pool" "${POOL_SPACE_MAP[$pool]}")  更新: $(date '+%m-%d %H:%M:%S')"
        done
        notify "[状态] 硬盘状态"
    fi
done
