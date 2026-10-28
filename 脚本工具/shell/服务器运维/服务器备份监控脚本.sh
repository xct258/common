#!/bin/bash
# ================================================================
# 服务器备份监控脚本（优化版）
#
# 相对原版的改动：
#   1. 增加单实例锁（flock），避免开机自启与手动执行并发操作同一备份目录
#   2. 配置文件缺失/加载失败时记录日志并跳过本轮，不再带着空变量继续运行
#   3. 压缩改用目录本身递归（"$dir" 而非 "$dir"/*），可包含 .env/.git 等隐藏文件
#   4. 校验 7zz 是否存在，逐条校验压缩结果，失败项不参与同步
#   5. 修复全角冒号：%H：%M -> %H:%M
#   6. 内置 JSON 转义 + curl 超时 + 失败重试，去掉对 jq 的硬依赖
#   7. 保留份数/远端/路径/调度时间/缓存目录等改为可配置（带默认值，兼容旧配置）
#   8. df 结果做数字校验；剩余时间计算前校验日期有效性
#   9. 增加日志 /var/log/server-backup.log
#  10. 同名备份目录自动加后缀，避免不同路径 basename 冲突
#  11. 同步失败或 rclone 缺失时，把本地 .7z 保留到 $FAILED_BACKUP_DIR（默认 /root/backup-failed）
#  12. flock 缺失时只告警，不再误报"已有备份任务在运行"
#
# 调试：ONCE=1 bash 本脚本  只执行一轮后退出
# ================================================================

set -uo pipefail

# ===== 可配置项（均可被 /root/apps/脚本/config/服务器基本信息.txt 覆盖） =====
CONFIG_FILE="${CONFIG_FILE:-/root/apps/脚本/config/服务器基本信息.txt}"
LOG_FILE="${LOG_FILE:-/var/log/server-backup.log}"
PUSH_URL="${PUSH_URL:-https://push-server.o1-1.xct258.top/api/push}"
PUSH_RETRIES="${PUSH_RETRIES:-3}"
LOCK_FILE="${LOCK_FILE:-/run/server-backup.lock}"
SCHEDULE_TIME="${SCHEDULE_TIME:-04:20}"
BACKUP_CACHE_DIR="${BACKUP_CACHE_DIR:-/home/xct258/备份}"
BACKUP_ERROR_DIR="${BACKUP_ERROR_DIR:-/tmp/backup_error}"
BACKUP_RETENTION="${BACKUP_RETENTION:-5}"
FAILED_BACKUP_DIR="${FAILED_BACKUP_DIR:-/root/backup-failed}"
RCLONE_REMOTE="${RCLONE_REMOTE:-openlist-webdav}"
SEVEN_ZIP="${SEVEN_ZIP:-7zz}"

# 磁盘监控默认值（可在配置文件中用 check_directories / threshold_map 覆盖）
check_directories=("/:根目录")
declare -A threshold_map=([根目录]=30)

# 跨轮次保存上一次的空间占用，用于计算增减
declare -A previous_space_map

mkdir -p "$(dirname "$LOG_FILE")" 2>/dev/null || true

log() {
  local ts; ts=$(date '+%F %T')
  echo "[$ts] $*" | tee -a "$LOG_FILE" >&2
}

# 最小 JSON 字符串转义（覆盖引号、反斜杠、换行、制表符）
json_escape() {
  local s=$1
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\n'/\\n}
  s=${s//$'\r'/\\r}
  s=${s//$'\t'/\\t}
  printf '%s' "$s"
}

push_message() {
  local sn=$1 topic=$2 msg=$3 payload attempt
  payload=$(printf '{"server_name":"%s","topic":"%s","message":"%s","push_to_mqtt":true,"msg_type":"markdown","mode":"append"}' \
    "$(json_escape "$sn")" "$(json_escape "$topic")" "$(json_escape "$msg")")
  for ((attempt=1; attempt<=PUSH_RETRIES; attempt++)); do
    if curl -fsS --max-time 10 -X POST "$PUSH_URL" \
        -H "Content-Type: application/json" -d "$payload" >/dev/null 2>&1; then
      return 0
    fi
    [ "$attempt" -lt "$PUSH_RETRIES" ] && sleep 2
  done
  log "消息推送失败（已重试 ${PUSH_RETRIES} 次）"
  return 1
}

load_config() {
  if [ ! -r "$CONFIG_FILE" ]; then
    log "配置文件不存在或不可读: $CONFIG_FILE"
    return 1
  fi
  # shellcheck disable=SC1090
  if ! . "$CONFIG_FILE"; then
    log "配置文件加载失败（可能存在语法错误）: $CONFIG_FILE"
    return 1
  fi
  # 兼容未声明的数组，避免 set -u 报错
  if ! declare -p backup_directories >/dev/null 2>&1; then
    backup_directories=()
  fi
  : "${server_name:=未知服务器}"
  : "${BACKUP_PASSWORD:=xct258}"
  if [ "$BACKUP_PASSWORD" = "xct258" ]; then
    log "警告：正在使用默认 7z 密码，请在 $CONFIG_FILE 中设置 BACKUP_PASSWORD"
  fi
  return 0
}

acquire_lock() {
  if ! command -v flock >/dev/null 2>&1; then
    log "警告：未找到 flock，跳过单实例锁（请安装 util-linux）"
    return 0
  fi
  mkdir -p "$(dirname "$LOCK_FILE")" 2>/dev/null || true
  if ! exec 9>"$LOCK_FILE"; then
    log "无法创建锁文件: $LOCK_FILE"
    exit 1
  fi
  if ! flock -n 9; then
    log "已有备份任务在运行，本次退出"
    exit 0
  fi
}

cleanup() {
  if [ "${KEEP_CACHE:-0}" = "1" ]; then
    rm -rf "$BACKUP_ERROR_DIR" 2>/dev/null || true
    return 0
  fi
  rm -rf "$BACKUP_CACHE_DIR" "$BACKUP_ERROR_DIR" 2>/dev/null || true
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM

run_once() {
  if ! load_config; then
    return 1
  fi

  local rclone_path="${RCLONE_PATH:-ssd-1/xct258/备份/$server_name}"

  if ! command -v "$SEVEN_ZIP" >/dev/null 2>&1; then
    log "未找到 7z 命令（$SEVEN_ZIP），跳过本轮备份"
    return 1
  fi

  mkdir -p "$BACKUP_CACHE_DIR" "$BACKUP_ERROR_DIR" || { log "创建临时目录失败"; return 1; }

  # ---- 压缩 ----
  local -a backup_file_paths=()
  local -A used_names=()
  local backup_dir base candidate n backup_file_name
  for backup_dir in "${backup_directories[@]}"; do
    [ -n "$backup_dir" ] || continue
    if [ ! -d "$backup_dir" ]; then
      log "备份目录不存在，跳过: $backup_dir"
      continue
    fi
    if [ -z "$(ls -A "$backup_dir" 2>/dev/null)" ]; then
      log "备份目录为空，跳过: $backup_dir"
      continue
    fi
    base=$(basename "$backup_dir")
    candidate="$base"; n=2
    while [ -n "${used_names[$candidate]:-}" ]; do
      candidate="${base}_${n}"; n=$((n+1))
    done
    used_names[$candidate]=1
    backup_file_name="$BACKUP_CACHE_DIR/${candidate}.7z"
    rm -f "$backup_file_name"
    # 用目录本身递归，包含隐藏文件
    if "$SEVEN_ZIP" a -p"$BACKUP_PASSWORD" -r -mhe "$backup_file_name" "$backup_dir" >/dev/null; then
      log "压缩完成: $backup_dir -> ${candidate}.7z"
      backup_file_paths+=("$backup_file_name")
    else
      log "压缩失败: $backup_dir"
    fi
  done

  # ---- 同步到远端 ----
  local backup_completion_time backup_message
  local -a failed_files=()
  KEEP_CACHE=0
  backup_completion_time=$(date +%Y-%m-%d_%H:%M)
  if [ ${#backup_file_paths[@]} -eq 0 ]; then
    backup_message="本轮没有可同步的备份文件"
  elif command -v rclone >/dev/null 2>&1; then
    local -a remote_lines=()
    local all_ok=true f dest
    for f in "${backup_file_paths[@]}"; do
      dest="$RCLONE_REMOTE:$rclone_path/$backup_completion_time"
      if rclone copy "$f" "$dest" >> "$BACKUP_ERROR_DIR/sync_result.txt" 2>&1; then
        remote_lines+=("- $(basename "$f") 成功")
      else
        remote_lines+=("- $(basename "$f") 失败")
        failed_files+=("$f")
        all_ok=false
      fi
    done
    if [ "$all_ok" = true ]; then
      local remote_list
      if remote_list=$(rclone lsf "$RCLONE_REMOTE:$rclone_path/" --dirs-only 2>/dev/null); then
        local -a dirs=()
        [ -n "$remote_list" ] && mapfile -t dirs < <(printf '%s\n' "$remote_list" | sed 's#/$##' | sort)
        if [ ${#dirs[@]} -gt "$BACKUP_RETENTION" ]; then
          local remove_count=$(( ${#dirs[@]} - BACKUP_RETENTION )) i
          for ((i=0; i<remove_count; i++)); do
            log "清理远端旧备份: ${dirs[i]}"
            rclone purge "$RCLONE_REMOTE:$rclone_path/${dirs[i]}" >> "$BACKUP_ERROR_DIR/sync_result.txt" 2>&1 || log "清理失败: ${dirs[i]}"
          done
        fi
      else
        log "列出远端备份目录失败，跳过清理"
      fi
    fi
    backup_message=$(printf '%s\n' "${remote_lines[@]}")
  else
    backup_message="rclone 未安装，跳过同步"
    failed_files=("${backup_file_paths[@]}")
    all_ok=false
  fi

  # ---- 同步失败/未同步的文件保留到本地，避免直接删除丢失备份 ----
  if [ ${#failed_files[@]} -gt 0 ]; then
    local ff
    if mkdir -p "$FAILED_BACKUP_DIR" 2>/dev/null; then
      for ff in "${failed_files[@]}"; do
        if mv -f "$ff" "$FAILED_BACKUP_DIR/" 2>/dev/null; then
          log "未同步备份已保留: $FAILED_BACKUP_DIR/$(basename "$ff")"
        else
          log "未同步备份保留失败: $ff"
        fi
      done
      backup_message+=$(printf '\n- 未同步文件已保留到 %s' "$FAILED_BACKUP_DIR")
    else
      log "创建保留目录失败: $FAILED_BACKUP_DIR，未同步文件暂留在 $BACKUP_CACHE_DIR"
      KEEP_CACHE=1
    fi
  fi

  if [ "${KEEP_CACHE:-0}" = "1" ]; then
    rm -rf "$BACKUP_ERROR_DIR"
  else
    rm -rf "$BACKUP_CACHE_DIR" "$BACKUP_ERROR_DIR"
  fi

  # ---- 磁盘监控 ----
  local message_df=$'**目录剩余空间:**'
  local dir_config_df check_dir check_name space_usage threshold
  local space_change space_change_message
  for dir_config_df in "${check_directories[@]}"; do
    IFS=':' read -r check_dir check_name <<< "$dir_config_df"
    space_usage=$(df -BG "$check_dir" 2>/dev/null | awk 'NR==2{print $4}' | tr -dc '0-9')
    if ! [[ "$space_usage" =~ ^[0-9]+$ ]]; then
      log "读取磁盘空间失败: $check_dir"
      continue
    fi
    threshold="${threshold_map[$check_name]:-100}"
    if [ "${previous_space_map[$check_name]:-0}" -ne 0 ]; then
      space_change=$(( space_usage - previous_space_map[$check_name] ))
      if [ "$space_change" -gt 0 ]; then
        space_change_message="（+${space_change}GB）"
      elif [ "$space_change" -lt 0 ]; then
        space_change_message="（${space_change}GB）"
      else
        space_change_message=""
      fi
    else
      space_change_message=""
    fi
    message_df+=$'\n'"- ${check_name}: ${space_usage}GB"
    [ "$space_usage" -lt "$threshold" ] && message_df+=" ⚠"
    message_df+="$space_change_message"
    previous_space_map[$check_name]=$space_usage
  done

  # ---- 剩余时间 ----
  local message_free_time=""
  if [ -z "${server_free_datetime:-}" ]; then
    log "未设置服务器可用时间，跳过剩余时间计算"
  else
    local target_ts current_ts remaining days hours months rem
    if target_ts=$(date -d "$server_free_datetime" +%s 2>/dev/null); then
      current_ts=$(date +%s)
      remaining=$(( target_ts - current_ts ))
      if [ "$remaining" -lt 0 ]; then
        days=$(( (-remaining) / 86400 ))
        hours=$(( ((-remaining) % 86400) / 3600 ))
        message_free_time="**⚠ 服务器已超过免费时间:** $days 天 $hours 小时"
      else
        days=$(( remaining / 86400 ))
        hours=$(( (remaining % 86400) / 3600 ))
        if [ "$days" -ge 30 ]; then
          months=$(( days / 30 ))
          rem=$(( days % 30 ))
          message_free_time="**服务器剩余免费时间:** $months 个月 $rem 天 $hours 小时"
        else
          message_free_time="**服务器剩余免费时间:** $days 天 $hours 小时"
        fi
      fi
    else
      log "server_free_datetime 格式无效: $server_free_datetime"
    fi
  fi

  # ---- 推送 ----
  local completion_time message
  completion_time=$(date +"%Y-%m-%d %H:%M")
  message=$(printf '## %s\n%s\n\n%s\n\n%s\n\n%s' \
    "$server_name" "$completion_time" "$backup_message" "$message_df" "$message_free_time")
  push_message "$server_name" "server/$server_name" "$message"
  return 0
}

acquire_lock
log "备份监控脚本启动（PID $$）"

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
