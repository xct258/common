#!/bin/bash

# 用 rclone listremotes 获取所有 remote 名称，去掉末尾冒号，读入数组
mapfile -t REMOTES < <(rclone listremotes | sed 's/:$//' | sort)

# 白名单（超过95%但不提醒，显示“已处理”）
WHITELIST=(
  "onedrive-video-4"
  # 可以继续添加更多白名单网盘名称
)

# 定时推送时间点（24小时制）
schedule_sleep_time="00:00"

# 判断某元素是否在数组里，$1=元素 $2=数组名
function in_array() {
  local element="$1"
  local arr_name="$2"
  local i
  eval "local arr=(\"\${${arr_name}[@]}\")"
  for i in "${arr[@]}"; do
    if [[ "$i" == "$element" ]]; then
      return 0
    fi
  done
  return 1
}

declare -A last_free_map

while true; do
  HOSTNAME=$(hostname)
  NOW=$(date "+%Y-%m-%d %H:%M:%S")

  output="*TrueNAS-1*
$NOW
网盘剩余空间：
"

  over_limit_remotes=()

  for remote in "${REMOTES[@]}"; do
    json=$(rclone about "${remote}:" --json 2>/dev/null)

    if [[ $? -ne 0 || -z "$json" ]]; then
      output+="
$remote
剩余空间: 获取失败
使用率: 获取失败
"
      continue
    fi

    total=$(echo "$json" | jq -r '.total // 0')
    used=$(echo "$json" | jq -r '.used // 0')
    free=$(echo "$json" | jq -r '.free // 0')

    human_free=$(numfmt --to=iec --suffix=B "$free" 2>/dev/null)

    if [[ "$total" -gt 0 ]]; then
      percent=$(awk "BEGIN { printf \"%.2f\", ($used/$total)*100 }")
    else
      percent="未知"
    fi

    delta_display=""
    if [[ -n "${last_free_map[$remote]}" ]]; then
      delta=$(( free - last_free_map[$remote] ))
      if [[ $delta -ne 0 ]]; then
        sign="+"; [[ $delta -lt 0 ]] && sign="-"
        delta_abs=${delta#-}
        human_delta=$(numfmt --to=iec --suffix=B "$delta_abs" 2>/dev/null)
        delta_display=" (${sign}${human_delta})"
      fi
    fi
    last_free_map["$remote"]=$free

    # 判断是否超过95%
    if [[ "$percent" != "未知" && $(awk "BEGIN {print ($percent >= 95)}") -eq 1 ]]; then
      # 判断是否在白名单
      if in_array "$remote" WHITELIST; then
        status="使用率: ${percent}%（已超95%，已处理）"
        # 不加入超限提醒列表
      else
        status="⚠️ 使用率: ${percent}%（已超95%，请及时处理）"
        over_limit_remotes+=("$remote")
      fi
    else
      status="使用率: ${percent}%"
    fi

    output+="
$remote
剩余空间: $human_free$delta_display
$status
"
  done

  if [[ ${#over_limit_remotes[@]} -gt 0 ]]; then
    limit_list=""
    for r in "${over_limit_remotes[@]}"; do
      limit_list+="$r
"
    done

    output+="

⚠️以下网盘超过设定值，请注意！⚠️
$limit_list
"
  fi

  clear
  echo "$output"

  curl -s -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=TrueNAS1" \
    -d "description=OneDrive空间状态推送" \
    -d "channel=nas通知" \
    --data-urlencode "content=$output" >/dev/null

  # 计算到下一个推送时间点的秒数
  current_date=$(date +%Y-%m-%d)
  target_time="${current_date} $schedule_sleep_time"
  sleep_seconds=$(( $(date -d "$target_time" +%s) - $(date +%s) ))
  if (( sleep_seconds < 0 )); then
    sleep_seconds=$(( sleep_seconds + 86400 ))
  fi

  sleep $sleep_seconds
done
