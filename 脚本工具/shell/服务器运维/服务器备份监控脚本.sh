#!/bin/bash

# 7z 加密密码（从配置文件读取，未设置则使用默认值）
BACKUP_PASSWORD=${BACKUP_PASSWORD:-xct258}

# 循环开始
while true; do
  # 读取服务器基本信息
  source /root/apps/脚本/config/服务器基本信息.txt
  # 设置需要删除的日志文件路径
  cache_logs=(
  # 不参与备份的日志
  )

  # 循环遍历数组，删除日志文件
  for cache_log in "${cache_logs[@]}"; do
    rm -rf "$cache_log"*
  done

  # 使用循环检测目录是否存在，如果不存在则创建
  for backup_dir_1 in "${backup_directories[@]}"; do
    if [ ! -d "$backup_dir_1" ]; then
      mkdir -p "$backup_dir_1"
    fi
  done

  # 定义临时目录
  backup_cache_dir="/home/xct258/备份"
  backup_error_dir="/tmp/backup_error"

  # 创建临时目录
  mkdir -p "$backup_cache_dir"
  mkdir -p "$backup_error_dir"

  # 执行备份操作
  backup_file_paths=()
  for backup_dir_2 in "${backup_directories[@]}"; do
    if [ -d "$backup_dir_2" ] && [ -z "$(ls -A "$backup_dir_2" 2>/dev/null)" ]; then
      continue
    fi
    backup_file_name="$backup_cache_dir/$(basename "$backup_dir_2").7z"
    7zz a -p"$BACKUP_PASSWORD" -r -mhe "$backup_file_name" "$backup_dir_2"/* >/dev/null
    backup_file_paths+=("$backup_file_name")
  done

  # 计算备份文件总大小（字节）
  total_backup_size=0
  for f in "${backup_file_paths[@]}"; do
    s=$(stat -c%s "$f" 2>/dev/null)
    total_backup_size=$((total_backup_size + s))
  done

  # 备份完成时间
  backup_completion_time=$(date +%Y-%m-%d_%H：%M)

  # 同步到 openlist-webdav
  RCLONE_REMOTE="openlist-webdav"
  RCLONE_PATH="ssd-1/xct258/备份/$server_name"
  backup_message=""
  if command -v rclone &>/dev/null; then
    remote_result=""
    all_ok=true
    for backup_file_rclone in "${backup_file_paths[@]}"; do
      dest_path="$RCLONE_REMOTE:$RCLONE_PATH/$backup_completion_time"
      if rclone copy "$backup_file_rclone" "$dest_path" >> "$backup_error_dir/sync_result.txt" 2>&1; then
        remote_result+="- $(basename "$backup_file_rclone") 成功
"
      else
        remote_result+="- $(basename "$backup_file_rclone") 失败
"
        all_ok=false
      fi
    done
    if [ "$all_ok" = true ]; then
      mapfile -t dirs < <(rclone lsf "$RCLONE_REMOTE:$RCLONE_PATH/" --dirs-only 2>/dev/null)
      if [ ${#dirs[@]} -gt 5 ]; then
        mapfile -t old_dirs < <(printf "%s\n" "${dirs[@]}" | sort | head -n -5)
        for old_dir in "${old_dirs[@]}"; do
          rclone purge "$RCLONE_REMOTE:$RCLONE_PATH/$old_dir" >> "$backup_error_dir/sync_result.txt" 2>&1
        done
      fi
    fi
    backup_message="$remote_result"
  else
    backup_message="rclone 未安装，跳过同步"
  fi

  # 删除临时目录
  rm -rf "$backup_cache_dir"
  rm -rf "$backup_error_dir"

  # 检测目录剩余空间
  # 定义检测剩余空间的目录和自定义名称的数组
  check_directories=(
    "/:根目录"
  )
  declare -A threshold_map  # 定义关联数组来存储目录和对应的阈值
  # 设置不同目录的阈值，不设置默认100
  threshold_map=(
    [根目录]=30
  )
  # 定义全局变量来保存上一次的空间使用情况
  declare -A previous_space_map
  message_df=""
  message_df="**目录剩余空间:**"
  for ((i = 0; i < ${#check_directories[@]}; i++)); do
    dir_config_df=${check_directories[i]}
    IFS=':' read -ra check_directory <<< "$dir_config_df"
    space_usage=$(df -BG "${check_directory[0]}" | awk 'NR==2{print $4}' | sed 's/G$//')
    threshold=${threshold_map[${check_directory[1]}]-100}
    if [[ ${previous_space_map[${check_directory[1]}]-0} -ne 0 ]]; then
      space_change=$((space_usage - previous_space_map[${check_directory[1]}]))
      if [[ $space_change -gt 0 ]]; then
        space_change_message="（+${space_change}GB）"
      elif [[ $space_change -lt 0 ]]; then
        space_change_message="（${space_change}GB）"
      else
        space_change_message=""
      fi
    else
      space_change_message=""
    fi
    message_df+="
- ${check_directory[1]}: ${space_usage}GB"
    [[ $space_usage -lt $threshold ]] && message_df+=" ⚠"
    message_df+="${space_change_message}"
    previous_space_map[${check_directory[1]}]=$space_usage
  done

  # 检查 server_free_datetime 是否为空
  if [[ -z "$server_free_datetime" ]]; then
    echo "没有设定服务器可用时间，跳过剩余时间计算。"
  else
    # 将目标日期和时间转换为时间戳
    target_timestamp_free=$(date -d "$server_free_datetime" +%s)
    current_timestamp_free=$(date +%s)

    # 计算剩余的秒数
    remaining_seconds_free=$((target_timestamp_free - current_timestamp_free))

    # 判断是否超过指定时间
    if [[ $remaining_seconds_free -lt 0 ]]; then

      # 计算超过的天数和小时数
      days_free=$(((-remaining_seconds_free) / (60 * 60 * 24)))
      hours_free=$(((-remaining_seconds_free) % (60 * 60 * 24) / (60 * 60)))

      message_free_time="**⚠ 服务器已超过免费时间:** $days_free 天 $hours_free 小时"
    else
      days_free=$((remaining_seconds_free / (60 * 60 * 24)))
      hours_free=$((remaining_seconds_free % (60 * 60 * 24) / (60 * 60)))
      if [[ $days_free -ge 30 ]]; then
        months_free=$((days_free / 30))
        remaining_days_free=$((days_free % 30))
        message_free_time="**服务器剩余免费时间:** $months_free 个月 $remaining_days_free 天 $hours_free 小时"
      else
        message_free_time="**服务器剩余免费时间:** $days_free 天 $hours_free 小时"
      fi
    fi
  fi

  # 当前时间
  completion_time=$(date +"%Y-%m-%d %H:%M")

  # 消息合并（Markdown 格式）
  message="## $server_name
$completion_time

$backup_message

$message_df

$message_free_time"

  # 推送消息
  curl -s -X POST "https://push-server.o1-1.xct258.top/api/push" \
  -H "Content-Type: application/json" \
  -d "$(jq -n --arg sn "$server_name" --arg topic "server/$server_name" --arg msg "$message" '{
    server_name: $sn,
    topic: $topic,
    message: $msg,
    push_to_mqtt: true,
    msg_type: "markdown",
    mode: "append"
  }')" >/dev/null

  # 定义脚本睡眠到指定时间
  schedule_sleep_time="04:20"
  # 休眠到特定时间
  current_date=$(date +%Y-%m-%d)
  target_time="${current_date} $schedule_sleep_time"
  time_difference=$(( $(date -d "${target_time}" +%s) - $(date +%s) ))
  if [[ ${time_difference} -lt 0 ]]; then
    time_difference=$(( ${time_difference} + 86400 ))
  fi
  sleep ${time_difference}
done
