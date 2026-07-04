#!/bin/bash
set -x

# 统一配置文件路径（请确保该文件存在）
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
config_file="$script_dir/备份配置.conf"

if [ ! -f "$config_file" ]; then
    echo "配置文件未找到: $config_file" >&2
    exit 1
fi

# 直接 source 配置文件
# shellcheck source=/dev/null
source "$config_file"

# 以上配置项请在配置文件中调整。
while true; do
  # 开始时间
  backup_start_time=$(date +%Y/%m/%d/%H:%M:%S)
  # 存放目录合并
  backup_dir_logs=$backup_logs/$backup_start_time
  for backup_file_sh_1 in "${backup_file_sh[@]}"; do
    # 脚本日志存放位置
    backup_file_sh_1_log=$backup_dir_logs/$backup_file_sh_1.log
    # 创建日志存放文件夹
    mkdir -p $backup_dir_logs/
    # 写入脚本开始时间到日志
    echo "$(date)" > $backup_file_sh_1_log 2>&1
    echo "----------------------------" >> $backup_file_sh_1_log 2>&1
    # 执行脚本并写入执行日志（传递调度脚本目录路径）
    $backup_dir/$backup_file_sh_1.sh "$script_dir" >> $backup_file_sh_1_log 2>&1
    # 写入脚本结束时间到日志
    echo "----------------------------" >> $backup_file_sh_1_log 2>&1
    echo "$(date)" >> $backup_file_sh_1_log 2>&1
  done
  current_date=$(date +%Y-%m-%d)
  target_time="${current_date} $schedule_sleep_time"
  time_difference=$(( $(date -d "$target_time" +%s) - $(date +%s) ))
  if [[ $time_difference -lt 0 ]]; then
    time_difference=$(( $time_difference + 86400 ))
  fi
  sleep $time_difference
done
