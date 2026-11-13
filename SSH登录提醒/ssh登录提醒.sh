#!/bin/bash
# ================================================================
# SSH 登录提醒（优化版）
#
# 相对原版的改动：
#   1. 仅在 SSH 会话中执行（检测 SSH_CONNECTION），并按连接去重，
#      避免 sudo -i / 多次 login shell 造成重复推送
#   2. 配置文件缺失/加载失败时记录日志并跳过，不再带着空 server_name 推送
#   3. 内置 JSON 转义，去掉对 jq 的硬依赖
#   4. curl 增加超时与失败重试
#   5. 全部逻辑放入后台子 shell 执行并 disown，不阻塞登录、不污染登录 shell 的 set 选项
#   6. 定期清理过期的去重标记文件
#
# 注意：本脚本由 /etc/profile.d 以 source 方式加载，切勿在顶层直接 set -e/-u。
# ================================================================

_ssh_login_notify() {
  set -uo pipefail

  local CONFIG_FILE="${CONFIG_FILE:-/root/apps/脚本/config/服务器基本信息.txt}"
  local LOG_FILE="${LOG_FILE:-/var/log/ssh-notify.log}"
  local PUSH_URL="${PUSH_URL:-https://push-server.o1-1.xct258.top/api/push}"
  local PUSH_RETRIES="${PUSH_RETRIES:-3}"

  mkdir -p "$(dirname "$LOG_FILE")" 2>/dev/null || true
  log() { local t; t=$(date '+%F %T'); echo "[$t] $*" >> "$LOG_FILE"; }

  # 读取服务器名称
  local server_name=""
  if [ -r "$CONFIG_FILE" ]; then
    # shellcheck disable=SC1090
    . "$CONFIG_FILE" || log "配置文件加载失败: $CONFIG_FILE"
  else
    log "配置文件不存在: $CONFIG_FILE，跳过推送"
    return 0
  fi
  [ -n "${server_name:-}" ] || { log "未配置 server_name，跳过推送"; return 0; }

  # 收集信息
  local ipv4_address ipv6_address username1 login_info username2 ip_address date_time
  ipv4_address=$(curl -4 -s --connect-timeout 5 --max-time 8 api.ipify.org 2>/dev/null || true)
  ipv6_address=$(curl -6 -s --connect-timeout 5 --max-time 8 api6.ipify.org 2>/dev/null || true)
  username1=$(whoami)
  login_info=$(who am i 2>/dev/null || true)
  if [ -n "$login_info" ]; then
    username2=$(printf '%s\n' "$login_info" | awk '{print $1}')
    ip_address=$(printf '%s\n' "$login_info" | awk '{gsub(/[()]/,"",$NF); print $NF}')
  else
    username2=""
    ip_address=""
  fi
  date_time=$(date +"%Y-%m-%d %H:%M")

  # 构造消息
  local message
  if [ -n "$username2" ] && [ "$username1" != "$username2" ]; then
    message=$(printf '## %s\n%s\n\n**⚠ 登陆提醒 ⚠**\n\n- **用户**: %s → %s（sudo提权）\n- **登录IP**: %s\n- **服务器IPv4**: %s\n- **服务器IPv6**: %s\n' \
      "$server_name" "$date_time" "$username2" "$username1" "${ip_address:-无法获取}" "$ipv4_address" "$ipv6_address")
  else
    message=$(printf '## %s\n%s\n\n**⚠ 登陆提醒 ⚠**\n\n- **用户**: %s\n- **登录IP**: %s\n- **服务器IPv4**: %s\n- **服务器IPv6**: %s\n' \
      "$server_name" "$date_time" "$username1" "${ip_address:-无法获取}" "$ipv4_address" "$ipv6_address")
  fi

  # 最小 JSON 转义
  json_escape() {
    local s=$1
    s=${s//\\/\\\\}
    s=${s//\"/\\\"}
    s=${s//$'\n'/\\n}
    s=${s//$'\r'/\\r}
    s=${s//$'\t'/\\t}
    printf '%s' "$s"
  }

  local payload attempt
  payload=$(printf '{"server_name":"%s","topic":"%s","message":"%s","push_to_mqtt":true,"msg_type":"markdown","mode":"append"}' \
    "$(json_escape "$server_name")" "$(json_escape "server/$server_name")" "$(json_escape "$message")")

  for ((attempt=1; attempt<=PUSH_RETRIES; attempt++)); do
    if curl -fsS --max-time 10 -X POST "$PUSH_URL" \
        -H "Content-Type: application/json" -d "$payload" >/dev/null 2>&1; then
      log "登录提醒已推送"
      return 0
    fi
    [ "$attempt" -lt "$PUSH_RETRIES" ] && sleep 2
  done
  log "登录提醒推送失败（已重试 ${PUSH_RETRIES} 次）"
  return 1
}

# 仅 SSH 会话；按连接去重（同步创建标记，避免并发/重复登录重复推送）
if [ -n "${SSH_CONNECTION:-}" ]; then
  _ssh_login_notify_marker="/tmp/.ssh_notify_$(printf '%s' "$SSH_CONNECTION" | md5sum | awk '{print $1}')"
  if [ ! -f "$_ssh_login_notify_marker" ]; then
    : > "$_ssh_login_notify_marker" 2>/dev/null || true
    # 清理 1 天前的旧标记
    find /tmp -maxdepth 1 -name '.ssh_notify_*' -mtime +1 -delete 2>/dev/null || true
    # 后台执行，避免阻塞登录；disown 防止退出登录时被 SIGHUP 中断
    _ssh_login_notify >/dev/null 2>&1 &
    disown 2>/dev/null || true
  fi
  unset _ssh_login_notify_marker
fi
unset -f _ssh_login_notify
