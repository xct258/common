#!/bin/bash

# 7z 加密密码（可在此修改）
BACKUP_PASSWORD="your_backup_password"

# 循环开始
while true; do
  # 读取服务器基本信息
  source /root/apps/脚本/config/服务器基本信息.txt
  # 设置需要删除的日志文件路径
  cache_logs=(
  # 不参与备份的日志
  "/home/xct258/docker/nginx-proxy-manager/data/logs/"
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
    backup_file_name="$backup_cache_dir/$(basename "$backup_dir_2").7z"
    7zz a -p"$BACKUP_PASSWORD" -r -mhe "$backup_file_name" "$backup_dir_2"/* >/dev/null
    backup_file_paths+=("$backup_file_name")
  done

  # 备份完成时间
  backup_completion_time=$(date +%Y-%m-%d_%H:%M)

  # 定义要同步到的Rclone远程路径
  rclone_remote_path1="备份/服务器/$server_name"
  # 执行rclone同步（如果配置了rclone_config1）
  if [ -n "$rclone_config1" ]; then
    exit_codes=()
    for backup_file_rclone in "${backup_file_paths[@]}"; do
      rclone copy "$backup_file_rclone" "$rclone_config1:$rclone_remote_path1/$backup_completion_time" >> "$backup_error_dir/sync_result.txt" 2>&1
      exit_codes+=("$?")
    done

    # 检查是否有出现异常的命令，有异常出现读取日志
    all_successful_rclone=true
    for code_rclone in "${exit_codes[@]}"; do
      if [ "$code_rclone" -ne 0 ]; then
        error_info_rclone=$(cat "$backup_error_dir/sync_result.txt")
        all_successful_rclone=false
        break
      fi
    done

    if [ "$all_successful_rclone" = true ]; then
      backup_message="备份成功🎉"
    else
      backup_message="备份失败⚠%0A"
      backup_message+="错误信息%0A"
      backup_message+="$error_info_rclone"
    fi
  else
    backup_message="未配置rclone，跳过同步"
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
  # 清空消息内容
  message_df=""
  message_df="目录剩余空间:"
  for ((i = 0; i < ${#check_directories[@]}; i++)); do
    dir_config_df=${check_directories[i]}
    IFS=':' read -ra check_directory <<< "$dir_config_df"  # 使用IFS来处理包含空格的目录和描述
    space_usage=$(df -BG "${check_directory[0]}" | awk 'NR==2{print $4}' | sed 's/G$//')  # 获取当前目录的可用空间（以GB为单位）
    threshold=${threshold_map[${check_directory[1]}]-100}  # 获取目录对应的阈值，如果不存在则默认为100
    # 检查空间使用情况是否与上一次检查时相同
    if [[ ${previous_space_map[${check_directory[1]}]-0} -ne 0 ]]; then
      space_change=$((space_usage - previous_space_map[${check_directory[1]}]))  # 计算空间变化量
      if [[ $space_change -gt 0 ]]; then
        space_change_message="（%2B${space_change}GB）"
      elif [[ $space_change -lt 0 ]]; then
        space_change_message="（${space_change}GB）"
      else
        space_change_message=""
      fi
    else
      space_change_message=""
    fi
    if [[ $space_usage -lt $threshold ]]; then
      message_df+="%0A${check_directory[1]}%0A"
      message_df+="可用空间为${space_usage}GB⚠${space_change_message}"
    else
      message_df+="%0A${check_directory[1]}%0A"
      message_df+="可用空间为${space_usage}GB${space_change_message}"
    fi
    # 更新上一次的空间使用情况
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

      message_free_time="⚠警告:服务器已超过免费时间:%0A$days_free天$hours_free小时!"
    else
      # 计算剩余的天数和小时数
      days_free=$((remaining_seconds_free / (60 * 60 * 24)))
      hours_free=$((remaining_seconds_free % (60 * 60 * 24) / (60 * 60)))

      # 判断是否可以转化为月
      if [[ $days_free -ge 30 ]]; then
        months_free=$((days_free / 30))
        remaining_days_free=$((days_free % 30))
        message_free_time="服务器剩余免费时间:%0A$months_free个月$remaining_days_free天$hours_free小时"
      else
        message_free_time="服务器剩余免费时间:%0A$days_free天$hours_free小时"
      fi
    fi
  fi

  # 当前时间
  completion_time=$(date +"%Y-%m-%d %H:%M")

  # 消息合并
  message="$server_name%0A"
  message+="$completion_time%0A"
  message+="%0A$backup_message%0A"
  message+="%0A$message_df%0A"
  message+="%0A$message_free_time"

  # 推送消息
  curl -s -X POST "https://msgpusher.xct258.top/push/root" \
  -d "title=$server_name&description=每日推送&channel=一般通知&content=${message}" >/dev/null

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
