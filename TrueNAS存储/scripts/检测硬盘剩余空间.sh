#!/bin/bash

# 初始监控目录（目录:别名）
check_directories=(
  "/:根目录"
)

# 目录对应阈值，单位GB，默认50GB
declare -A threshold_map=(
  [根目录]=5
)

# 白名单：这些目录不显示警告符号，提示内容为“已处理”
whitelist_names=(
  "hhd-1"
)

# 保存上一次的剩余空间，key=别名
declare -A previous_space_map

# 根目录剩余空间变量
root_space=0

# 定时推送时间点（24小时制）
schedule_sleep_time="00:00"

# 动态添加 /mnt 目录下的文件夹（格式：/mnt/xxx:xxx）
add_mnt_directories() {
  for dir in /mnt/*; do
    if [[ -d "$dir" && ! "${check_directories[*]}" =~ "$dir" ]]; then
      folder_name=$(basename "$dir")
      check_directories+=("$dir:$folder_name")
    fi
  done
}

generate_message() {
  local name=$1
  local space=$2
  local change_msg=$3
  local extra_msg=$4

  local warn_icon="⚠"
  local note="请处理"

  if is_whitelisted "$name"; then
    warn_icon=""
    note="已处理"
  fi

  local prefix=""
  if [[ -n "$warn_icon" ]]; then
    prefix="$warn_icon "
  fi

  case "$extra_msg" in
    *"剩余空间不足"*)
      message_df+="
$name
${prefix}可用空间为${space}GB${change_msg}
剩余空间低于设定值，$note
"
      ;;
    *"与根目录空间一致"*)
      message_df+="
$name
${prefix}可用空间为${space}GB${change_msg}
与根目录空间一致，可能需要解锁，$note
"
      ;;
    *)
      message_df+="
$name
可用空间为${space}GB${change_msg}
"
      ;;
  esac
}

is_whitelisted() {
  local name=$1
  for w in "${whitelist_names[@]}"; do
    if [[ "$w" == "$name" ]]; then
      return 0
    fi
  done
  return 1
}


# 发送消息到 msgpusher
send_msgpusher() {
  local content="$1"
  curl -s -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=TrueNAS1" \
    -d "description=每日推送" \
    -d "channel=nas通知" \
    --data-urlencode "content=$content" >/dev/null
}

# 获取当前时间字符串
get_current_time() {
  date +"%Y-%m-%d %H:%M:%S"
}

# 生成整条消息，拼接所有目录信息
generate_full_message() {
  local current_time=$1
  message_df="*TrueNAS-1*
$current_time
目录剩余空间:
"
  for ((i=0; i < ${#check_directories[@]}; i++)); do
    IFS=':' read -r directory name <<< "${check_directories[i]}"

    # 目录不存在则提示异常
    if [[ ! -d "$directory" ]]; then
      message_df+="
$name
异常，请检查⚠
"
      continue
    fi

    # 获取剩余空间（GB整数）
    space=$(df -BG "$directory" | awk 'NR==2{print $4}' | sed 's/G//')

    # 阈值，默认50
    threshold=${threshold_map[$name]:-50}

    # 记录根目录空间
    if [[ "$name" == "根目录" ]]; then
      root_space=$space
    fi

    # 计算剩余空间变化
    change_msg=""
    if [[ -n "${previous_space_map[$name]}" ]]; then
      delta=$(( space - previous_space_map[$name] ))
      if (( delta > 0 )); then
        change_msg="（+${delta}GB）"
      elif (( delta < 0 )); then
        change_msg="（${delta}GB）"
      fi
    fi

    # 判断是否与根目录空间相同（非根目录）
    extra_msg=""
    if [[ "$space" -eq "$root_space" && "$name" != "根目录" ]]; then
      extra_msg="，与根目录空间一致，可能需要解锁，请检查⚠"
    elif (( space < threshold )); then
      extra_msg="，剩余空间不足，请处理⚠"
    fi

    generate_message "$name" "$space" "$change_msg" "$extra_msg"

    # 更新上一次剩余空间
    previous_space_map[$name]=$space
  done
}

# 提取核心目录剩余空间内容，去掉括号内内容，用于对比是否有变化
extract_core_message() {
  echo "$1" | sed -n '/目录剩余空间:/,$p' | sed '1d' | sed 's/（[^）]*）//g'
}

# 主循环
last_message=""
first_run=true

while true; do
  current_time=$(get_current_time)

  # 动态添加/mnt下目录
  add_mnt_directories

  # 生成消息内容
  generate_full_message "$current_time"

  # 提取当前核心消息内容（剔除括号内差异信息）
  current_core=$(extract_core_message "$message_df")

  # 只有首次或消息有变动时发送推送
  if [[ "$first_run" == true || "$current_core" != "$(extract_core_message "$last_message")" ]]; then
    send_msgpusher "$message_df"
    last_message="$message_df"
    first_run=false
  fi

  # 计算到下一个推送时间点的秒数
  current_date=$(date +%Y-%m-%d)
  target_time="${current_date} $schedule_sleep_time"
  sleep_seconds=$(( $(date -d "$target_time" +%s) - $(date +%s) ))
  if (( sleep_seconds < 0 )); then
    sleep_seconds=$(( sleep_seconds + 86400 ))
  fi

  sleep $sleep_seconds
done
