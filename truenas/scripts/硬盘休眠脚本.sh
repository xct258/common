#!/usr/bin/env bash

# ##################################################
# TrueNAS 硬盘自动停转（Spindown）脚本
# 作用：监控硬盘 I/O 活动，并在设定的闲置时间后强制硬盘停转（进入待机模式）。
# 
# 适用系统：TrueNAS CORE (FreeBSD) 和 TrueNAS SCALE (Linux)
# 版本: 2.5.0 (中文版)
# ##################################################

VERSION="2.5.0 (CN)"
TIMEOUT=30              # 默认超时时间（秒）：硬盘闲置多久后停转（默认 1 小时）
POLL_TIME=5             # 默认采样间隔（秒）：每隔多久检查一次硬盘读写（默认 10 分钟）
IGNORED_DRIVES=""         # 命令行参数传入的排除列表
MANUAL_MODE=0             # 手动模式：0 = 自动检测所有硬盘，1 = 仅监控指定的硬盘
ONESHOT_MODE=0            # 单次执行模式：运行一次检测后立即退出
CHECK_MODE=0              # 状态检查模式：在每个周期输出硬盘当前的电源状态
QUIET=0                   # 静音模式：不输出任何日志
VERBOSE=0                 # 详细模式：输出更多调试信息
LOG_TO_SYSLOG=0           # 系统日志：1 = 写入 syslog，0 = 屏幕输出
DRYRUN=0                  # 测试模式：1 = 只模拟动作，不真正停转硬盘
SHUTDOWN_TIMEOUT=0        # 关机超时：所有硬盘停转多久后关闭服务器（0为禁用）

# === 硬盘排除列表 (硬编码白名单) ===
# 在此处填入硬盘序列号（Serial Number），这些硬盘将永远不会被停转。
EXCLUDE_LIST=(
    "2BKDN9JN"
    "2CG3HVMN"
)

# 定义用于存储数据的关联数组
declare -A DRIVES           # 存储检测到的硬盘及其协议 (ATA 或 SCSI)
declare -A ZFSPOOLS         # 存储要监控的 ZFS 池
declare -A DRIVES_BY_POOLS  # 映射关系：池名称 -> 物理硬盘列表
declare -A DRIVEID_TO_DEV   # 映射关系：硬盘 ID (UUID/GPTID) -> 设备名 (sda/ada0)

HOST_PLATFORM=              # 自动检测的平台：FreeBSD 或 Linux
DRIVEID_TYPE=               # 硬盘 ID 类型：gptid 或 partuuid
OPERATION_MODE=disk         # 运行模式：disk (按盘) 或 zpool (按池)
DISK_CTRL_TOOL=             # 硬盘控制工具：camcontrol, hdparm 或 smartctl

##
# 打印使用说明
##
function print_usage() {
    cat << EOF
使用方法:
  $0 [-h] [-q] [-v] [-l] [-d] [-o] [-c] [-m] [-u 模式] [-t 超时] [-p 间隔] [-i 排除项] [-s 关机时间] [-x 工具]

参数详解:
  -t 秒数      : 硬盘闲置多久后停转 (默认: 3600秒)。
  -p 秒数      : 每次检查 I/O 的采样间隔 (默认: 600秒)。
  -s 秒数      : 自动关机超时。若所有监控硬盘停转超过此时长，系统将关机。
  -u 模式      : 'disk' (默认，按设备名监控) 或 'zpool' (按 ZFS 池名监控)。
  -i 名称      : 排除某个硬盘/池；若开启手动模式(-m)，则表示仅监控该项。
  -m           : 开启手动模式。此时 -i 参数变为“白名单”。
  -o           : 单次执行模式。检测一次后立即处理并退出。
  -c           : 状态检查模式。周期性显示硬盘当前的电源状态。
  -q           : 静音模式（不输出日志）。
  -v           : 详细调试模式。
  -l           : 将日志发送到系统日志 (syslog)。
  -d           : 测试运行模式。仅模拟，不会真的停转硬盘。
  -x 工具      : 强制使用指定工具 (camcontrol, hdparm, smartctl)。
  -h           : 显示此帮助信息。
EOF
}

##
# 日志记录函数
##
function log() {
    if [[ $QUIET -eq 0 ]]; then
        if [[ $LOG_TO_SYSLOG -eq 1 ]]; then
            echo "$1" | logger -i -t "spindown_timer"
        else
            echo "[$(date '+%F %T')] $1"
        fi
    fi
}

function log_verbose() {
    if [[ $VERBOSE -eq 1 ]]; then
        log "[调试] $1"
    fi
}

function log_error() {
    if [[ $LOG_TO_SYSLOG -eq 1 ]]; then
        echo "[错误]: $1" | logger -i -t "spindown_timer"
    else
        >&2 echo "[$(date '+%F %T')] [错误]: $1"
    fi
}

##
# 检测系统平台
##
function detect_host_platform() {
    if [[ "$(uname)" == "Linux" ]]; then
        HOST_PLATFORM=Linux
    elif [[ "$(uname)" == "FreeBSD" ]]; then
        HOST_PLATFORM=FreeBSD
    else
        log_error "不支持的操作系统类型: $(uname)。尝试按 Linux 处理..."
        HOST_PLATFORM=Linux
        return
    fi
    log_verbose "检测到系统平台: $HOST_PLATFORM"
}

##
# 检测硬盘控制工具
##
detect_disk_ctrl_tool() {
    local SUPPORTED_TOOLS=("camcontrol" "smartctl" "hdparm")

    if [[ " ${SUPPORTED_TOOLS[@]} " =~ " ${DISK_CTRL_TOOL} " ]]; then
        if which "$DISK_CTRL_TOOL" &> /dev/null; then
            echo "$DISK_CTRL_TOOL"
            return
        else
            log_error "指定的工具 $DISK_CTRL_TOOL 未安装或未找到。"
            return
        fi
    fi

    for tool in "${SUPPORTED_TOOLS[@]}"; do
        if which "$tool" &> /dev/null; then
            echo "$tool"
            return
        fi
    done

    log_error "未找到支持的硬盘控制工具。"
    return
}

##
# 检测硬盘 ID 类型
##
function detect_driveid_type() {
    if [[ -n $(which glabel) ]]; then
        DRIVEID_TYPE=gptid
    elif [[ -d "/dev/disk/by-partuuid/" ]]; then
        DRIVEID_TYPE=partuuid
    else
        log_error "无法识别硬盘 ID 类型，退出中..."
        exit 1
    fi
    log_verbose "检测到硬盘 ID 类型: $DRIVEID_TYPE"
}

##
# 创建硬盘 ID 到设备名 (ada/sd) 的映射
##
function populate_driveid_to_dev_array() {
    case $DRIVEID_TYPE in
        "gptid")
            log_verbose "使用 glabel 创建硬盘映射 (CORE)"
            while read -r row; do
                local gptid=$(echo "$row" | cut -d ' ' -f1)
                local diskid=$(echo "$row" | cut -d ' ' -f3 | rev | cut -d 'p' -f2 | rev)
                if [[ "$gptid" = "gptid"* ]]; then
                    DRIVEID_TO_DEV[$gptid]=$diskid
                fi
            done < <(glabel status | tail -n +2 | tr -s ' ')
        ;;
        "partuuid")
            log_verbose "使用 partuuid 创建硬盘映射 (SCALE)"
            while read -r row; do
                local partuuid=$(basename -- "${row}")
                local dev=$(basename -- "$(readlink -f "${row}")" | sed "s/[0-9]\+$//")
                DRIVEID_TO_DEV[$partuuid]=$dev
            done < <(find /dev/disk/by-partuuid/ -type l)
        ;;
    esac
}

##
# 注册并识别硬盘协议，同时自动过滤固态硬盘 (SSD)
##
function register_drive() {
    local drive="$1"
    if [ -z "$drive" ]; then return 1; fi

    # --- 新增：检测是否为固态硬盘 ---
    local IS_ROTATIONAL=1
    case $HOST_PLATFORM in
        "Linux")
            # 在 Linux 下检查 /sys/block/设备名/queue/rotational
            # 1 代表机械硬盘 (HDD)，0 代表固态硬盘 (SSD/NVMe)
            if [[ -f "/sys/block/$drive/queue/rotational" ]]; then
                IS_ROTATIONAL=$(cat "/sys/block/$drive/queue/rotational")
            fi
            ;;
        "FreeBSD")
            # 在 FreeBSD 下使用 camcontrol 检查介质类型
            # 如果输出包含 "Solid State" 则为 SSD
            if camcontrol identify "$drive" | grep -q "Solid State"; then
                IS_ROTATIONAL=0
            fi
            ;;
    esac

    if [[ "$IS_ROTATIONAL" -eq 0 ]]; then
        log_verbose "跳过固态硬盘: $drive (检测为非旋转介质)"
        return 0
    fi
    # --- 检测结束 ---

    local DISK_IS_ATA
    case $DISK_CTRL_TOOL in
        "camcontrol") DISK_IS_ATA=$(camcontrol identify $drive |& grep -E "^protocol(.*)ATA");;
        "hdparm") DISK_IS_ATA=$(hdparm -I "/dev/$drive" |& grep -E "^ATA device");;
        "smartctl") DISK_IS_ATA=$(smartctl -i "/dev/$drive" |& grep -E "ATA V");;
    esac

    if [[ -n $DISK_IS_ATA ]]; then
        DRIVES[$drive]="ATA"
    else
        DRIVES[$drive]="SCSI"
    fi
    log_verbose "已注册机械硬盘: $drive (协议: ${DRIVES[$drive]})"
}

##
# 硬盘模式下的自动识别逻辑
##
function detect_drives_disk() {
    local DRIVE_IDS
    local EXCLUDE_REGEX=$(IFS='|'; echo "${EXCLUDE_LIST[*]}")

    if [[ $MANUAL_MODE -eq 1 ]]; then
        DRIVE_IDS=" ${IGNORED_DRIVES} "
    else
        local ALL_PHYSICAL_DRIVES=$(lsblk -dno NAME,SERIAL | grep -E '^(ada|da|sd)')
        if [[ -n "$EXCLUDE_REGEX" ]]; then
            DRIVE_IDS=$(echo "$ALL_PHYSICAL_DRIVES" | grep -Ev "$EXCLUDE_REGEX" | awk '{print $1}' | tr '\n' ' ')
        else
            DRIVE_IDS=$(echo "$ALL_PHYSICAL_DRIVES" | awk '{print $1}' | tr '\n' ' ')
        fi
        DRIVE_IDS=" ${DRIVE_IDS} " 
        for drive in ${IGNORED_DRIVES[@]}; do
            DRIVE_IDS=$(sed "s/ ${drive} / /g" <<< "${DRIVE_IDS}")
        done
    fi

    for drive in ${DRIVE_IDS}; do
        register_drive "$drive"
    done
}

##
# ZFS 池模式下的自动识别逻辑
##
function detect_drives_zpool() {
    if [[ $MANUAL_MODE -eq 1 ]]; then
        for poolname in $IGNORED_DRIVES; do
            ZFSPOOLS[${#ZFSPOOLS[@]}]="$poolname"
        done
    else
        local poolnames=$(zpool list -H -o name)
        for ignored_pool in $IGNORED_DRIVES; do
            poolnames=${poolnames//$ignored_pool/}
        done
        for poolname in $poolnames; do
            ZFSPOOLS[${#ZFSPOOLS[@]}]="$poolname"
        done
    fi

    for poolname in ${ZFSPOOLS[*]}; do
        local disks
        if ! disks=$(zpool list -H -v "$poolname"); then
            log_error "无法获取 ZFS 池 $poolname 的信息。"
            continue;
        fi

        while read -r driveid; do
            case $DRIVEID_TYPE in
                "gptid")    driveid=$(echo "$driveid" | grep -E "^gptid/.*$" | sed "s/^\(.*\)\.eli$/\1/") ;;
                "partuuid") driveid=$(echo "$driveid" | grep -E "^(\w+\-){2,}") ;;
            esac
            if [ -z "$driveid" ]; then continue; fi
            if [[ "${DRIVEID_TO_DEV[$driveid]}" == "nvme"* ]]; then continue; fi

            register_drive "${DRIVEID_TO_DEV[$driveid]}"
            DRIVES_BY_POOLS[$poolname]="${DRIVES_BY_POOLS[$poolname]} ${DRIVEID_TO_DEV[$driveid]}"
        done < <(echo "$disks" | tr -s "\\t" " " | cut -d ' ' -f2)
    done
}

function get_drives() { echo "${!DRIVES[@]}"; }

##
# 获取闲置硬盘列表
##
function get_idle_drives() {
    local IOSTAT_OUTPUT
    local ACTIVE_DRIVES
    case $OPERATION_MODE in
        "disk")
            IOSTAT_OUTPUT=$(iostat -x -z -d $1 2)
            case $HOST_PLATFORM in
                "FreeBSD")
                    local CUT_OFFSET=$(grep -no "extended device statistics" <<< "$IOSTAT_OUTPUT" | tail -n1 | cut -d: -f1)
                    CUT_OFFSET=$((CUT_OFFSET+2))
                    ;;
                "Linux")
                    local CUT_OFFSET=$(grep -no "Device" <<< "$IOSTAT_OUTPUT" | tail -n1 | cut -d: -f1)
                    CUT_OFFSET=$((CUT_OFFSET+1))
                    ;;
            esac
            ACTIVE_DRIVES=$(sed -n "${CUT_OFFSET},\$p" <<< "$IOSTAT_OUTPUT" | cut -d' ' -f1 | tr '\n' ' ')
        ;;
        "zpool")
            IOSTAT_OUTPUT=$(zpool iostat -H ${ZFSPOOLS[*]} $1 2)
            while read -r row; do
                local poolname=$(echo "$row" | cut -d ' ' -f1)
                local reads=$(echo "$row" | cut -d ' ' -f4)
                local writes=$(echo "$row" | cut -d ' ' -f5)
                if [ "$reads" != "0" ] || [ "$writes" != "0" ]; then
                    ACTIVE_DRIVES="$ACTIVE_DRIVES ${DRIVES_BY_POOLS[$poolname]}"
                fi
            done < <(tail -n +$((${#ZFSPOOLS[@]}+1)) <<< "${IOSTAT_OUTPUT}" | tr -s "\\t" " ")
        ;;
    esac

    local IDLE_DRIVES=" $(get_drives) " 
    for drive in ${ACTIVE_DRIVES}; do
        IDLE_DRIVES=`sed "s/ ${drive} / /g" <<< ${IDLE_DRIVES}`
    done
    echo ${IDLE_DRIVES}
}

function is_ata_drive() { if [[ ${DRIVES[$1]} == "ATA" ]]; then echo 1; else echo 0; fi; }

##
# 检测硬盘是否正在旋转 (1=正在旋转, 0=已停转)
##
function drive_is_spinning() {
    case $DISK_CTRL_TOOL in
        "camcontrol")
            if [[ $(is_ata_drive $1) -eq 1 ]]; then
                if [[ -z $(camcontrol epc $1 -c status -P | grep 'Standby') ]]; then echo 1; else echo 0; fi
            else
                if [[ -z $(camcontrol modepage $1 -m 0x1a |& grep -E "^STANDBY(.*)1") ]]; then echo 1; else echo 0; fi
            fi
        ;;
        "hdparm")
            if [[ -z $(hdparm -C "/dev/$1" | grep 'standby') ]]; then echo 1; else echo 0; fi
        ;;
        "smartctl")
            if [[ -z $(smartctl --nocheck standby -i "/dev/$1" | grep -q 'Device is in STANDBY mode') ]]; then echo 1; else echo 0; fi
        ;;
    esac
}

##
# 执行硬盘停转动作
##
function spindown_drive() {
    if [[ $(drive_is_spinning $1) -eq 1 ]]; then
        if [[ $DRYRUN -eq 0 ]]; then
            case $DISK_CTRL_TOOL in
                "camcontrol")
                    if [[ $(is_ata_drive $1) -eq 1 ]]; then camcontrol standby $1; else camcontrol stop $1; fi
                ;;
                "hdparm")
                    hdparm -q -y "/dev/$1"
                ;;
                "smartctl")
                    if [[ $(is_ata_drive $1) -eq 1 ]]; then
                        smartctl --set=standby,now "/dev/$1"
                    else
                        smartctl -d scsi --set=standby,now "/dev/$1"
                    fi
                ;;
            esac
            log "已成功停转闲置硬盘: $1"
        else
            log "[模拟] 将停转硬盘: $1 (当前为测试模式，未执行物理操作)"
        fi
    else
        log_verbose "硬盘已处于停转状态: $1"
    fi
}

##
# 主程序逻辑
##
function main() {
    log "硬盘自动停转脚本 V$VERSION 启动"
    if [[ $DRYRUN -eq 1 ]]; then log "当前运行在 [测试模式]..."; fi

    detect_host_platform
    log_verbose "运行用户: $(whoami) (UID: $(id -u))"

    if [ "$EUID" -ne 0 ]; then
        log_error "本脚本必须以 root 权限运行。请使用 sudo。"
        exit 1
    fi

    DISK_CTRL_TOOL=$(detect_disk_ctrl_tool)
    if [[ -z $DISK_CTRL_TOOL ]]; then
        log_error "未找到适用的硬盘控制工具，退出中..."
        exit 1
    fi

    detect_driveid_type
    populate_driveid_to_dev_array
    detect_drives_$OPERATION_MODE

    if [[ ${#DRIVES[@]} -eq 0 ]]; then
        log_error "未检测到需要监控的硬盘。请检查白名单配置。"
        exit 1
    fi

    log "正在监控以下硬盘 (超时时间: ${TIMEOUT}秒): $(get_drives)"
    log "I/O 采样周期: ${POLL_TIME}秒"
    
    declare -A DRIVE_TIMEOUTS
    for drive in $(get_drives); do
        DRIVE_TIMEOUTS[$drive]=${TIMEOUT}
    done

while true; do
        # 获取当前闲置的硬盘列表
        local IDLE_DRIVES=$(get_idle_drives ${POLL_TIME})

        for drive in "${!DRIVE_TIMEOUTS[@]}"; do
            if [[ $IDLE_DRIVES =~ $drive ]]; then
                # 硬盘闲置：减少倒计时
                DRIVE_TIMEOUTS[$drive]=$((DRIVE_TIMEOUTS[$drive] - POLL_TIME))
                if [[ ! ${DRIVE_TIMEOUTS[$drive]} -gt 0 ]]; then
                    DRIVE_TIMEOUTS[$drive]=${TIMEOUT}
                    spindown_drive ${drive}
                fi
            else
                # === 核心修改部分：检测到硬盘活动 ===
                
                # 1. 重置倒计时
                DRIVE_TIMEOUTS[$drive]=${TIMEOUT}

                # 2. 读取并记录温度
                # 我们只在硬盘正在旋转时读取，避免因为读温度而意外唤醒正在休眠的盘
                if [[ $(drive_is_spinning $drive) -eq 1 ]]; then
                    # 尝试通过 smartctl 提取温度数值
                    local TEMP=$(smartctl -a "/dev/$drive" | grep -i "Temperature" | awk '{print $10}' | head -n 1)
                    
                    # 如果提取失败（不同型号硬盘输出格式不同），尝试通用解析
                    if [ -z "$TEMP" ]; then
                        TEMP=$(smartctl -A "/dev/$drive" | grep "Temperature_Celsius" | awk '{print $10}')
                    fi

                    if [ -n "$TEMP" ]; then
                        # 写入文件：日期 时间 设备名 温度
                        echo "[$(date '+%F %T')] Device: $drive, Temperature: ${TEMP}°C" >> /root/apps/脚本/hdd_temp_activity.log
                        log_verbose "检测到 $drive 活动，当前温度: ${TEMP}°C"
                    fi
                fi
            fi
        done

        if [[ $ONESHOT_MODE -eq 1 ]]; then exit 0; fi
        
        log_verbose "当前各盘倒计时: $(for x in "${!DRIVE_TIMEOUTS[@]}"; do printf "[%s]=%s " "$x" "${DRIVE_TIMEOUTS[$x]}" ; done)"
    done
}

# 解析命令行参数
while getopts ":hqvdlmoct:p:i:s:u:x:" opt; do
  case ${opt} in
    t ) TIMEOUT=${OPTARG} ;;
    p ) POLL_TIME=${OPTARG} ;;
    i ) IGNORED_DRIVES="$IGNORED_DRIVES ${OPTARG}" ;;
    s ) SHUTDOWN_TIMEOUT=${OPTARG} ;;
    o ) ONESHOT_MODE=1 ;;
    c ) CHECK_MODE=1 ;;
    q ) QUIET=1 ;;
    v ) VERBOSE=1 ;;
    l ) LOG_TO_SYSLOG=1 ;;
    d ) DRYRUN=1 ;;
    m ) MANUAL_MODE=1 ;;
    u ) OPERATION_MODE=${OPTARG} ;;
    x ) DISK_CTRL_TOOL=${OPTARG} ;;
    h|\?|: ) print_usage; exit ;;
  esac
done

main