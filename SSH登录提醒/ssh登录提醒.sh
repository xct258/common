#!/bin/bash
# ================================================================
# SSH 登录提醒（push-server 版）
#
# 推送地址固定为 https://push-server.o1-1.xct258.top/api/push
# 请求字段遵循 push-server 接口定义（API.md）：
#   server_name / category / topic / title / message / status
#   push_to_mqtt / msg_type / mode
#
# 特点：
#   * 只在 SSH 会话中执行（检测 SSH_CONNECTION），按连接去重，
#     避免 sudo -i / 多次 login shell 重复推送
#   * 服务器名优先读取配置，缺失时回退到 hostname
#   * 内置 JSON 转义，不依赖 jq
#   * curl 超时 + 重试，失败写日志
#   * 后台子 shell 执行并 disown，不阻塞登录
#
# 注意：本脚本由 /etc/profile.d 以 source 方式加载，顶层不要 set -e/-u。
# ================================================================

_ssh_login_notify() {
  set -uo pipefail

  local PUSH_URL="https://push-server.o1-1.xct258.top/api/push"
  local TOPIC="server/ssh-login"
  local TITLE="SSH 登录提醒"
  local CONFIG_FILE="${SSH_NOTIFY_CONFIG:-/root/apps/脚本/config/服务器基本信息.txt}"
  local LOG_FILE="${SSH_NOTIFY_LOG:-/var/log/ssh-notify.log}"
  local PUSH_RETRIES=3

  mkdir -p "$(dirname "$LOG_FILE")" 2>/dev/null || true
  log() { printf '[%s] %s\n' "$(date '+%F %T')" "$*" >> "$LOG_FILE"; }

  # ---------- 服务器名：配置 > hostname ----------
  local server_name=""
  if [ -r "$CONFIG_FILE" ]; then
    server_name=$(
      # shellcheck source=/dev/null
      . "$CONFIG_FILE" >/dev/null 2>&1 || true
      printf '%s' "${server_name:-}"
    )
  fi
  [ -n "$server_name" ] || server_name=$(hostname 2>/dev/null || echo unknown)

  # ---------- 登录信息 ----------
  local client_ip="" user login_user v4 v6 now
  client_ip="${SSH_CONNECTION%% *}"          # 第一段是客户端 IP
  user=$(whoami 2>/dev/null || echo unknown)
  login_user="${SUDO_USER:-}"                # sudo -i 时的原始用户
  now=$(date '+%Y-%m-%d %H:%M:%S')
  v4=$(curl -4 -s --connect-timeout 5 --max-time 8 https://api.ipify.org 2>/dev/null || true)
  v6=$(curl -6 -s --connect-timeout 5 --max-time 8 https://api6.ipify.org 2>/dev/null || true)

  # ---------- 正文（服务端会自动加【服务通知】头，不要手写） ----------
  local message
  message=$(printf '**时间**：%s\n**用户**：%s%s\n**来源 IP**：%s\n**服务器 IPv4**：%s\n**服务器 IPv6**：%s' \
    "$now" "$user" \
    "${login_user:+（sudo 提权自 ${login_user}）}" \
    "${client_ip:-未知}" "${v4:-未获取}" "${v6:-未获取}")

  # ---------- 最小 JSON 转义 ----------
  json_escape() {
    local s=$1
    s=${s//\\/\\\\}
    s=${s//\"/\\\"}
    s=${s//$'\n'/\\n}
    s=${s//$'\r'/\\r}
    s=${s//$'\t'/\\t}
    printf '%s' "$s"
  }

  local payload attempt body
  payload=$(printf '{"server_name":"%s","category":"service","topic":"%s","title":"%s","message":"%s","status":"info","push_to_mqtt":true,"msg_type":"markdown","mode":"append"}' \
    "$(json_escape "$server_name")" \
    "$(json_escape "$TOPIC")" \
    "$(json_escape "$TITLE")" \
    "$(json_escape "$message")")

  for ((attempt = 1; attempt <= PUSH_RETRIES; attempt++)); do
    if body=$(curl -fsS --connect-timeout 5 --max-time 10 -X POST "$PUSH_URL" \
        -H 'Content-Type: application/json; charset=utf-8' \
        --data-binary "$payload" 2>&1); then
      log "登录提醒已推送：$body"
      return 0
    fi
    log "推送失败（第 ${attempt}/${PUSH_RETRIES} 次）：$body"
    [ "$attempt" -lt "$PUSH_RETRIES" ] && sleep 2
  done
  log "登录提醒推送失败（已重试 ${PUSH_RETRIES} 次）"
  return 1
}

# 仅 SSH 会话；按连接去重（先建标记再后台推送，避免并发重复）
# 登录路径只做纯 bash 的字符串处理，不 fork 外部命令，避免拖慢登录
if [ -n "${SSH_CONNECTION:-}" ]; then
  _ssh_login_notify_key=${SSH_CONNECTION//[^0-9A-Za-z]/_}
  _ssh_login_notify_marker="/tmp/.ssh_notify_${_ssh_login_notify_key}"
  if [ ! -e "$_ssh_login_notify_marker" ]; then
    : > "$_ssh_login_notify_marker" 2>/dev/null || true
    # 清理、探测、推送全部放进后台，且断开 stdin，避免占用终端
    (
      find /tmp -maxdepth 1 -name '.ssh_notify_*' -mtime +1 -delete 2>/dev/null || true
      _ssh_login_notify
    ) </dev/null >/dev/null 2>&1 &
    disown 2>/dev/null || true
  fi
  unset _ssh_login_notify_key _ssh_login_notify_marker
fi
unset -f _ssh_login_notify
