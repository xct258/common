#!/bin/bash
# ================================================================
# rclone 目录存档与云端清理（优化版）
#
# 相对原版的改动：
#   1. 增加单实例锁（flock），避免多实例并发清理/上传
#   2. 检测 BACKUP_PASSWORD 为空或仍是占位符时直接退出，避免用错密码加密
#   3. 压缩改用目录本身递归（含隐藏文件）
#   4. 7zz / rclone 失败均有检查与日志；压缩在临时目录完成，失败不留残档
#   5. 目录列表用 mapfile 读取，目录名含空格不再被拆错
#   6. 保留份数 / 远端路径 / 调度时间可配置（带默认值）
#   7. 增加日志 /var/log/rclone-archive.log
#   8. 上传失败时把 .7z 保留到 $FAILED_ARCHIVE_DIR（默认 /root/rclone-archive-failed）
#   9. flock 缺失时只告警，不再误报"已有任务在运行"
#
# 调试：ONCE=1 bash 本脚本  只执行一轮后退出
# ================================================================

set -uo pipefail

BACKUP_PASSWORD="${BACKUP_PASSWORD:-}"
REMOTE_PATH="${REMOTE_PATH:-onedrive2:备份/服务器}"
RCLONE_CONFIG1="${RCLONE_CONFIG1:-onedrive2:备份/服务器/pve-2-debian12-nginx}"
ARCHIVE_RETENTION="${ARCHIVE_RETENTION:-10}"
SCHEDULE_TIME="${SCHEDULE_TIME:-04:20}"
SEVEN_ZIP="${SEVEN_ZIP:-7zz}"
LOG_FILE="${LOG_FILE:-/var/log/rclone-archive.log}"
LOCK_FILE="${LOCK_FILE:-/run/rclone-archive.lock}"

# 目录存档配置：<rclone 路径>#<清单文件名>
archive_data=(
  "onedrivegame2:/galgame/熟肉#汉化游戏-onedrivegame2.txt"
  "onedrivegame2:/galgame/生肉#未汉化游戏-onedrivegame2.txt"
)

mkdir -p "$(dirname "$LOG_FILE")" 2>/dev/null || true
log() {
  local t; t=$(date '+%F %T')
  echo "[$t] $*" | tee -a "$LOG_FILE" >&2
}

# ---- 前置检查 ----
if [ -z "$BACKUP_PASSWORD" ] || [ "$BACKUP_PASSWORD" = "your_backup_password" ]; then
  log "错误：请先设置 BACKUP_PASSWORD（当前为空或仍是占位符）"
  exit 1
fi
if ! command -v "$SEVEN_ZIP" >/dev/null 2>&1; then
  log "错误：未找到 7z 命令（$SEVEN_ZIP）"
  exit 1
fi
if ! command -v rclone >/dev/null 2>&1; then
  log "错误：未找到 rclone"
  exit 1
fi

# ---- 单实例锁 ----
if ! command -v flock >/dev/null 2>&1; then
  log "警告：未找到 flock，跳过单实例锁（请安装 util-linux）"
else
  mkdir -p "$(dirname "$LOCK_FILE")" 2>/dev/null || true
  if ! exec 9>"$LOCK_FILE"; then
    log "无法创建锁文件: $LOCK_FILE"
    exit 1
  fi
  if ! flock -n 9; then
    log "已有任务在运行，退出"
    exit 0
  fi
fi

run_once() {
  local workdir backup_archive="重要目录存档" backup_archive_dir backup_completion_time
  workdir=$(mktemp -d) || { log "创建临时目录失败"; return 1; }
  backup_archive_dir="$workdir/$backup_archive"
  mkdir -p "$backup_archive_dir"

  # ---- 生成各网盘目录清单并打包 ----
  local entry directory cache_file file_list
  for entry in "${archive_data[@]}"; do
    IFS='#' read -r directory cache_file <<< "$entry"
    [ -n "$directory" ] || continue
    if ! file_list=$(rclone lsf "$directory" 2>/dev/null); then
      log "列出目录失败: $directory"
      continue
    fi
    printf '%s\n' "$file_list" > "$backup_archive_dir/$cache_file"
    log "已生成清单: $cache_file（$(printf '%s\n' "$file_list" | grep -c . || true) 项）"
  done

  if ! "$SEVEN_ZIP" a -p"$BACKUP_PASSWORD" -r -mhe "$workdir/$backup_archive.7z" "$backup_archive_dir" >/dev/null; then
    log "压缩失败: $backup_archive"
    rm -rf "$workdir"
    return 1
  fi

  backup_completion_time=$(date +%Y-%m-%d_%H:%M)
  local upload_ok=false
  if rclone move "$workdir/$backup_archive.7z" "$RCLONE_CONFIG1/$backup_completion_time"; then
    upload_ok=true
    log "已上传存档: $RCLONE_CONFIG1/$backup_completion_time"
  else
    local failed_dir="${FAILED_ARCHIVE_DIR:-/root/rclone-archive-failed}"
    mkdir -p "$failed_dir" 2>/dev/null || true
    if mv -f "$workdir/$backup_archive.7z" "$failed_dir/${backup_archive}_${backup_completion_time}.7z" 2>/dev/null; then
      log "上传存档失败，已保留本地副本: $failed_dir/${backup_archive}_${backup_completion_time}.7z"
    else
      log "上传存档失败，且本地副本保存失败"
    fi
  fi
  rm -rf "$workdir"

  # 上传失败时跳过云端清理，避免旧存档被删后新存档又没传上去
  if [ "$upload_ok" != true ]; then
    log "本轮上传失败，跳过云端旧目录清理"
    return 0
  fi

  # ---- 清理云端旧目录，仅保留最近 N 份 ----
  local remote_list dir count i
  if ! remote_list=$(rclone lsf "$REMOTE_PATH" --dirs-only 2>/dev/null); then
    log "列出远端顶层目录失败: $REMOTE_PATH"
    return 1
  fi
  local -a directories=()
  [ -n "$remote_list" ] && mapfile -t directories < <(printf '%s\n' "$remote_list" | sed 's#/$##' | sort)

  for dir in "${directories[@]}"; do
    [ -n "$dir" ] || continue
    log "处理目录: $dir"
    local folder_list
    if ! folder_list=$(rclone lsf "$REMOTE_PATH/$dir" --dirs-only 2>/dev/null); then
      log "列出子目录失败: $REMOTE_PATH/$dir"
      continue
    fi
    local -a folders=()
    [ -n "$folder_list" ] && mapfile -t folders < <(printf '%s\n' "$folder_list" | sed 's#/$##' | sort)
    count=${#folders[@]}
    if (( count > ARCHIVE_RETENTION )); then
      for ((i=0; i<count-ARCHIVE_RETENTION; i++)); do
        log "删除旧文件夹: ${REMOTE_PATH}/${dir}/${folders[i]}"
        if ! rclone purge "${REMOTE_PATH}/${dir}/${folders[i]}"; then
          log "删除失败: ${folders[i]}"
        fi
      done
    fi
  done
  return 0
}

if [ "${ONCE:-0}" = "1" ]; then
  run_once
  exit $?
fi

while true; do
  if ! run_once; then
    log "本轮执行失败，5 分钟后重试"
    sleep 300
    continue
  fi
  target_time="$(date +%F) $SCHEDULE_TIME"
  time_difference=$(( $(date -d "$target_time" +%s) - $(date +%s) ))
  if [ "$time_difference" -le 0 ]; then
    time_difference=$(( time_difference + 86400 ))
  fi
  log "下次执行: $(date -d "@$(( $(date +%s) + time_difference ))" '+%F %T')（休眠 ${time_difference}s）"
  sleep "$time_difference"
done
