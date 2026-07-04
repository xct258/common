#!/bin/bash

# 7z 加密密码（可在此修改）
BACKUP_PASSWORD="your_backup_password"

# 指定要处理的网盘路径
REMOTE_PATH="onedrive2:备份/服务器"

# 指定上传重要目录存档路径
rclone_config1="onedrive2:备份/服务器/pve-2-debian12-nginx"

# 定义目录存档路径
archive_data=(
  #"onedrive3:/音声#音声-onedrive3.txt"
  #"onedrivegame3:/galgame/熟肉#汉化游戏-onedrivegame3.txt"
  #"onedrivegame3:/galgame/生肉#未汉化游戏-onedrivegame3.txt"
  "onedrivegame2:/galgame/熟肉#汉化游戏-onedrivegame2.txt"
  "onedrivegame2:/galgame/生肉#未汉化游戏-onedrivegame2.txt"
)

while true; do
  message_error=""
  backup_archive="重要目录存档"
  backup_archive_dir="/root/重要目录存档"
  mkdir -p "$backup_archive_dir"

  # 执行目录存档操作
  for entry in "${archive_data[@]}"; do
    IFS='#' read -r directory cache_file <<< "$entry"
    file_list=$(rclone lsf "$directory")
    echo "$file_list" > "$backup_archive_dir/$cache_file"
  done

  7zz a -p"$BACKUP_PASSWORD" -r -mhe "$backup_archive.7z" "$backup_archive_dir"/*
  backup_completion_time=$(date +%Y-%m-%d_%H:%M)
  rclone move "$backup_archive.7z" "$rclone_config1/$backup_completion_time"
  rm -rf "$backup_archive_dir"

  # 列出顶层目录
  directories=$(rclone lsf "$REMOTE_PATH" --dirs-only)

  # 遍历每个目录
  for dir in $directories; do
    echo "处理目录: $dir"
    
    # 获取当前目录中所有的文件夹并存储到数组中
    mapfile -t folders < <(rclone lsf "$REMOTE_PATH/$dir" --dirs-only | sort)

    # 计算需要删除的文件夹数量
    count=${#folders[@]}
    folders_to_delete=()

    # 如果文件夹数量超过 10，则准备删除多余的文件夹
    if (( count > 10 )); then
      folders_to_delete=("${folders[@]:0:count-10}")
    fi

    # 删除除了最近 10 次的文件夹之外的所有文件夹
    for folder in "${folders_to_delete[@]}"; do
      echo "删除文件夹: ${REMOTE_PATH}/${dir}/${folder}"
      rclone purge "${REMOTE_PATH}/${dir}/${folder}"
    done
  done

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
