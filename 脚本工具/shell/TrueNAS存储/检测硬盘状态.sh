#!/bin/bash
# 🏷️ 要跳过的标签数组，包含不需要监控的硬盘标签（比如“boot-pool”）
SKIP_LABELS=("boot-pool")

declare -a DISK_PATHS
declare -A DISK_NAMES
declare -A LAST_CHANGE_TIME  # 记录每个硬盘上次变化时间

if [ ${#SKIP_LABELS[@]} -eq 0 ] || [[ -z "${SKIP_LABELS[0]}" ]]; then
  SKIP_REGEX="^$"
else
  SKIP_REGEX=$(IFS='|'; echo "${SKIP_LABELS[*]}")
fi

parse_disks() {
  local output
  output=$(lsblk -f | awk -v regex="$SKIP_REGEX" -v c=0 '
    /zfs_member/ {
      if (regex != "^$" && $4 ~ regex) { next }
      if ($4 == "") { next }
      label = ($4 == "cache1") ? $4 "-" ++c : $4
      gsub(/^[^a-zA-Z0-9]*/, "", $1)
      print $1, label
    }
  ')
  while read -r dev label; do
    path="/dev/$dev"
    DISK_PATHS+=("$path")
    DISK_NAMES["$path"]="$label"
  done <<< "$output"
}

send_msg() {
  local content="$1"
  curl -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=TrueNAS1&description=每日推送&channel=nas通知&content=${content}"
}

current_time() {
  date +"%Y-%m-%d %H:%M:%S"
}

get_disk_status() {
  sudo hdparm -C "$1" 2>/dev/null | awk '/drive state is/ {print $4}'
}

translate_status() {
  case "$1" in
    "active/idle") echo "唤醒 🚀" ;;
    "standby")     echo "休眠 💤" ;;
    *)             echo "$1" ;;
  esac
}

disk_name() {
  echo "${DISK_NAMES[$1]}"
}

escape_markdown() {
  local text="$1"
  text="${text//_/\\_}"
  echo "$text"
}

# 统一生成带硬盘名称的标签显示名
format_label_display() {
  local disk="$1"
  local label="$2"
  local count="$3"

  if (( count > 1 )); then
    local dev_name disk_name
    dev_name=$(basename "$disk")
    disk_name=$(lsblk -no PKNAME "/dev/$dev_name" 2>/dev/null)
    [[ -z "$disk_name" ]] && disk_name="${dev_name%%[0-9]*}"
    echo "${label}_${disk_name}"
  else
    echo "$label"
  fi
}

append_status_line() {
  local label="$1"
  local display="$2"
  local is_changed="$3"

  local safe_label
  safe_label=$(escape_markdown "$label")

  summary+="• **$safe_label**：$display
"
  if [[ "$is_changed" == "true" ]]; then
    changes+="• **$safe_label** → $display
"
  fi
}

format_message() {
  local now="$1"
  local changes="$2"
  local summary="$3"
  cat <<EOF
*📦 TrueNAS1 硬盘状态更新*
🕒 时间：$now
-----------------------------

*🌀 状态变化：*
$changes-----------------------------

*📊 当前硬盘状态总览：*
$summary
EOF
}

# 写入状态到配置文件
write_status_to_ini() {
  local ini_file="/mnt/ssd-1/docker/server-monitoring/配置文件/TrueNAS.ini"

  # 🔍 核心修改：如果文件不存在，仅提示并跳过写入，不影响主程序运行
  if [[ ! -f "$ini_file" ]]; then
    echo "[$(current_time)] ⚠️  提示: 配置文件不存在，跳过写入 INI。但监控和推送仍在运行。"
    return 0  # 结束本次函数调用，返回主循环
  fi

  local tmp_file="${ini_file}.tmp"
  local current_time_now
  current_time_now=$(current_time)
  local current_time_fmt=${current_time_now// /_}

  # --- 以下为你原有的逻辑，保持不变 ---
  local new_section="[硬盘运行情况]
时间=\"$current_time_fmt\"
"

  declare -A label_count=()
  for disk in "${DISK_PATHS[@]}"; do
    label=$(disk_name "$disk")
    ((label_count["$label"]++))
  done

  for disk in "${DISK_PATHS[@]}"; do
    local label status display label_display changed_at
    label=$(disk_name "$disk")
    status=$(get_disk_status "$disk")
    display=$(translate_status "$status")
    label_display=$(format_label_display "$disk" "$label" "${label_count["$label"]}")

    changed_at="${LAST_CHANGE_TIME["$disk"]:-$current_time_now}"
    new_section+="${label_display}=${display} ； 状态变化时间：${changed_at}
"
  done

  # 执行写入操作（只有文件存在时才会走到这一步）
  awk -v section="[硬盘运行情况]" -v newsec="$new_section" '
    BEGIN {in_section=0; found=0}
    {
      if ($0 ~ /^\[.*\]/) {
        if (in_section) {
          print newsec
          in_section=0
        }
        if ($0 == section) {
          in_section=1
          found=1
          next
        }
      }
      if (!in_section) print $0
    }
    END {
      if (in_section) print newsec
      if (!found) print "\n" newsec
    }
  ' "$ini_file" > "$tmp_file" && mv "$tmp_file" "$ini_file"
}


monitor_disks() {
  declare -A PREVIOUS_STATUS

  while true; do
    local has_change=false
    changes=""
    summary=""
    local now
    now=$(current_time)

    declare -A label_count=()
    for disk in "${DISK_PATHS[@]}"; do
      label=$(disk_name "$disk")
      ((label_count["$label"]++))
    done

    for disk in "${DISK_PATHS[@]}"; do
      local label status display prev label_display
      label=$(disk_name "$disk")
      status=$(get_disk_status "$disk")
      display=$(translate_status "$status")
      prev="${PREVIOUS_STATUS[$disk]}"
      label_display=$(format_label_display "$disk" "$label" "${label_count["$label"]}")

      if [[ "$display" != "$prev" ]]; then
        PREVIOUS_STATUS["$disk"]="$display"
        LAST_CHANGE_TIME["$disk"]="$now"
        has_change=true
        append_status_line "$label_display" "$display" true
      else
        append_status_line "$label_display" "$display" false
      fi
    done

    if $has_change; then
      local msg
      msg=$(format_message "$now" "$changes" "$summary")
      send_msg "$msg"
    fi

    write_status_to_ini

    sleep 60
  done
}

parse_disks
monitor_disks
