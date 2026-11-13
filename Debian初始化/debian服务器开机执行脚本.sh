#!/bin/bash
# ================================================================
# Debian 服务器初始化脚本（重构版）
#
# 包含：
#   * 时区 / 中文环境 / 基础软件 / 时间同步 / 内核符号链接清理
#   * 7zip（GitHub 官方仓库，按架构下载并校验）
#   * Docker（官方脚本 3 次 → 阿里云 apt 源 → 失败退出，可选）
#   * ufw（先探测 SSH 端口再启用）
#   * boot-exec 开机任务框架
#   * SSH 登录提醒（可选）
#   * OCI 附加 IPv4/IPv6（元数据自动读 IPv6，IPv4 手工录入，配置可保留，
#     配置完成后打印每个附加 IPv4 的出口公网 IP）
#
# 约定：
#   * 交互输入在非 TTY 环境下自动使用默认值，不会因 EOF 直接退出
#   * 所有下载：直连 3 次 → GitHub 域名套国内加速 → 仍失败退出
# ================================================================

set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

# ---------------- 常量 ----------------
LOG_FILE="/var/log/boot-init.log"
GITHUB_MIRRORS="${GITHUB_MIRRORS:-https://ghfast.top/ https://gh-proxy.com/ https://ghproxy.net/}"
OCI_CONF="/etc/oci-secondary-ips.conf"
OCI_SCRIPT="/usr/local/sbin/oci-secondary-ips"
OCI_UNIT="/etc/systemd/system/oci-secondary-ips.service"
BOOT_SCRIPT="/usr/local/bin/boot-exec.sh"
BOOT_UNIT="/etc/systemd/system/boot-exec.service"
BOOT_TASKS="/etc/boot-exec/tasks.list"
SSH_NOTIFY="/etc/profile.d/ssh_login_notify.sh"

WORK_DIR=""

# ---------------- 基础工具 ----------------
log() {
  local style="$1"; shift
  local text="$*"
  printf '%s %s\n' "$(date '+%F %T')" "$text" >> "$LOG_FILE"
  if [ -n "$style" ]; then
    printf '\e[%sm%s\e[0m\n' "$style" "$text"
  else
    printf '%s\n' "$text"
  fi
}

die() {
  log "31;1" "错误：$*"
  exit 1
}

cleanup() {
  local rc=$?
  [ -n "$WORK_DIR" ] && rm -rf "$WORK_DIR"
  log "" ">>> 脚本结束，退出码=$rc"
}

ask_yn() {
  # ask_yn <提示> <默认 y|n>
  local prompt="$1" default="${2:-n}" answer
  while true; do
    printf '%s [y/n]: ' "$prompt"
    if ! read -r answer; then
      echo
      [ "$default" = "y" ]
      return
    fi
    answer="${answer:-$default}"
    case "$answer" in
      y|Y) return 0 ;;
      n|N) return 1 ;;
      *) echo "请输入 y 或 n" ;;
    esac
  done
}

ask_value() {
  # ask_value <提示> <默认值>
  local prompt="$1" default="${2:-}" answer
  printf '%s%s: ' "$prompt" "${default:+ [$default]}" >&2
  if ! read -r answer; then
    echo
    printf '%s' "$default"
    return 0
  fi
  printf '%s' "${answer:-$default}"
}

valid_ipv4_cidr() {
  local value="$1" ip mask octet
  [[ "$value" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}/([0-9]{1,2})$ ]] || return 1
  ip="${value%/*}"
  mask="${value#*/}"
  [ "$mask" -ge 1 ] && [ "$mask" -le 32 ] || return 1
  IFS=. read -r -a octets <<< "$ip"
  for octet in "${octets[@]}"; do
    [ "$octet" -ge 0 ] && [ "$octet" -le 255 ] || return 1
  done
  return 0
}

# ---------------- 下载 ----------------
_is_github_url() {
  case "$1" in
    https://github.com/*|https://raw.githubusercontent.com/*|\
    https://objects.githubusercontent.com/*|https://codeload.github.com/*)
      return 0 ;;
    *) return 1 ;;
  esac
}

_dl_curl() {
  curl -fsSL --connect-timeout 10 --max-time 120 -o "$2" "$1"
}

download_try() {
  # download_try <url> <输出路径>
  local url="$1" out="$2" attempt prefix tmp
  tmp="${out}.part.$$"
  for attempt in 1 2 3; do
    if _dl_curl "$url" "$tmp"; then
      mv -f "$tmp" "$out"
      return 0
    fi
    log "33;1" "下载失败（第 $attempt/3 次）：$url"
    [ "$attempt" -lt 3 ] && sleep 3
  done
  if _is_github_url "$url"; then
    for prefix in $GITHUB_MIRRORS; do
      log "33;1" "直连失败，改用加速：${prefix}"
      if _dl_curl "${prefix}${url}" "$tmp"; then
        mv -f "$tmp" "$out"
        log "32;1" "加速下载成功：${prefix}"
        return 0
      fi
    done
  fi
  rm -f "$tmp"
  return 1
}

download_or_die() {
  local url="$1" out="$2"
  download_try "$url" "$out" || die "直连与加速均失败：$url"
}

# ---------------- 系统基础 ----------------
setup_timezone() {
  timedatectl set-timezone Asia/Shanghai
  log "32;1" "时区已设置为 Asia/Shanghai"
}

setup_locale() {
  apt-get install -y locales >/dev/null
  if locale 2>/dev/null | grep -q 'zh_CN.UTF-8'; then
    log "32;1" "当前已是中文环境"
    return 0
  fi
  grep -qxF 'zh_CN.UTF-8 UTF-8' /etc/locale.gen 2>/dev/null \
    || echo 'zh_CN.UTF-8 UTF-8' >> /etc/locale.gen
  locale-gen zh_CN.UTF-8 >/dev/null
  update-locale LANG=zh_CN.UTF-8
  # 直接在当前会话生效，不再要求重连/重跑
  export LANG=zh_CN.UTF-8 LC_ALL=zh_CN.UTF-8
  log "32;1" "中文环境已配置（当前会话已生效）"
}

install_base_packages() {
  apt-get install -y sudo wget nano ufw curl jq bc xz-utils \
    systemd-timesyncd isc-dhcp-client
}

setup_timesync() {
  systemctl enable --now systemd-timesyncd >/dev/null 2>&1 \
    || log "33;1" "时间同步服务启用失败（可能被 chrony 取代），已跳过"
}

clean_kernel_symlinks() {
  local link
  for link in /initrd.img /initrd.img.old /vmlinuz /vmlinuz.old; do
    if [ -L "$link" ]; then
      log "32;1" "删除不必要的符号链接 $link"
      rm -f "$link"
    fi
  done
}

# ---------------- 7zip ----------------
install_7zip() {
  if [ -e /bin/7zz ]; then
    log "32;1" "7z 已安装"
    return 0
  fi
  local tag file arch attempt pkg url
  tag=""
  for attempt in 1 2 3; do
    tag=$(curl -fsSL --connect-timeout 10 --max-time 20 \
      https://api.github.com/repos/ip7z/7zip/releases/latest 2>/dev/null \
      | grep -oP '"tag_name"\s*:\s*"\K[^"]+' | head -1 || true)
    [ -n "$tag" ] && break
    log "33;1" "获取 7zip 版本号失败（第 $attempt/3 次）"
    [ "$attempt" -lt 3 ] && sleep 3
  done
  [ -n "$tag" ] || die "无法获取 7zip 最新版本号"
  file=$(printf '%s' "$tag" | tr -d '.')
  case "$(uname -m)" in
    *x86_64*)  arch="x64" ;;
    *aarch64*) arch="arm64" ;;
    *) die "无法确定架构（$(uname -m)），请手动安装 7z" ;;
  esac
  pkg="7z${file}-linux-${arch}.tar.xz"
  url="https://github.com/ip7z/7zip/releases/download/${tag}/${pkg}"
  log "33;1" "下载 7zip ${tag}: $url"
  download_or_die "$url" "$WORK_DIR/7zz.tar.xz"
  mkdir -p "$WORK_DIR/7zz"
  tar -xf "$WORK_DIR/7zz.tar.xz" -C "$WORK_DIR/7zz" \
    || die "7z 解压失败"
  chmod +x "$WORK_DIR/7zz/7zz"
  "$WORK_DIR/7zz/7zz" i >/dev/null 2>&1 \
    || die "7z 可执行文件校验失败（架构不兼容？）"
  install -m 0755 "$WORK_DIR/7zz/7zz" /bin/7zz
  ln -sf /bin/7zz /usr/local/bin/7z
  log "32;1" "7zip 安装成功（$tag）"
}

# ---------------- Docker ----------------
install_docker_official() {
  local attempt
  for attempt in 1 2 3; do
    log "33;1" "官方源安装 Docker（第 $attempt/3 次）"
    if curl -fsSL --connect-timeout 10 --max-time 120 \
        https://get.docker.com -o "$WORK_DIR/get-docker.sh" \
       && bash "$WORK_DIR/get-docker.sh"; then
      log "32;1" "官方源安装 Docker 成功"
      return 0
    fi
    sleep 3
  done
  return 1
}

install_docker_aliyun() {
  local distro codename arch list key
  log "33;1" "官方源失败，回退阿里云镜像"
  distro=debian
  codename=$(. /etc/os-release && echo "${VERSION_CODENAME:-bookworm}")
  arch=$(dpkg --print-architecture)
  list="/etc/apt/sources.list.d/docker.list"
  key="/etc/apt/keyrings/docker.asc"

  # 镜像源可能还没有新发行版目录，回退 bookworm
  if ! curl -fsI --connect-timeout 10 --max-time 20 \
      "https://mirrors.aliyun.com/docker-ce/linux/${distro}/dists/${codename}/Release" \
      >/dev/null 2>&1; then
    log "33;1" "镜像源没有 ${codename}，回退 bookworm"
    codename="bookworm"
    curl -fsI --connect-timeout 10 --max-time 20 \
      "https://mirrors.aliyun.com/docker-ce/linux/${distro}/dists/${codename}/Release" \
      >/dev/null 2>&1 || return 1
  fi

  apt-get update || return 1
  apt-get install -y ca-certificates curl gnupg || return 1
  install -m 0755 -d /etc/apt/keyrings || return 1
  curl -fsSL --connect-timeout 10 --max-time 60 \
    "https://mirrors.aliyun.com/docker-ce/linux/${distro}/gpg" -o "$key" || return 1
  chmod a+r "$key"
  printf 'deb [arch=%s signed-by=%s] https://mirrors.aliyun.com/docker-ce/linux/%s %s stable\n' \
    "$arch" "$key" "$distro" "$codename" > "$list"
  if ! apt-get update \
     || ! apt-get install -y docker-ce docker-ce-cli containerd.io \
        docker-buildx-plugin docker-compose-plugin; then
    rm -f "$list"
    return 1
  fi
  systemctl enable --now docker >/dev/null 2>&1 || true
  log "32;1" "阿里云镜像安装 Docker 成功"
}

maybe_install_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "32;1" "Docker 已安装"
    return 0
  fi
  if ! ask_yn "Docker 未安装，是否安装" n; then
    log "32;1" "不安装 Docker"
    return 0
  fi
  if ! install_docker_official && ! install_docker_aliyun; then
    die "官方源与阿里云镜像均安装失败，请检查网络后手动安装"
  fi
  command -v docker >/dev/null 2>&1 || die "安装流程结束但未检测到 docker"
  log "32;1" "Docker 安装成功：$(docker --version)"
}

# ---------------- ufw ----------------
setup_ufw() {
  if LC_ALL=C ufw status 2>/dev/null | grep -q '^Status: active'; then
    log "32;1" "ufw 已启用"
    return 0
  fi
  local ssh_port=""
  if command -v sshd >/dev/null 2>&1; then
    ssh_port=$(sshd -T 2>/dev/null | awk '$1 == "port" {print $2; exit}' || true)
  fi
  if [ -z "$ssh_port" ] && command -v ss >/dev/null 2>&1; then
    ssh_port=$(LC_ALL=C ss -tnlp 2>/dev/null \
      | awk '/sshd/ {print $4}' | sed 's/.*://' | sort -un | head -1 || true)
  fi
  [ -n "$ssh_port" ] || ssh_port=22
  log "33;1" "检测到 SSH 端口：$ssh_port，放行 22/$ssh_port/80/443 后启用 ufw"
  ufw allow "${ssh_port}/tcp"
  [ "$ssh_port" != "22" ] && ufw allow 22/tcp
  ufw allow 80/tcp
  ufw allow 443/tcp
  ufw --force enable
  log "32;1" "ufw 已启用"
}

# ---------------- 开机任务框架 ----------------
install_boot_service() {
  cat > "$BOOT_SCRIPT" <<'EOF'
#!/bin/bash
# 逐行执行 /etc/boot-exec/tasks.list，# 开头与空行忽略
[ -f /etc/boot-exec/tasks.list ] || exit 0
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    ""|\#*) continue ;;
  esac
  echo "[boot-exec] 执行: $line"
  bash -c "$line" || echo "[boot-exec] 执行失败: $line"
done < /etc/boot-exec/tasks.list
EOF
  chmod 0755 "$BOOT_SCRIPT"

  cat > "$BOOT_UNIT" <<'EOF'
[Unit]
Description=boot-exec
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/bin/bash -c "/usr/local/bin/boot-exec.sh &"

[Install]
WantedBy=multi-user.target
EOF

  mkdir -p "$(dirname "$BOOT_TASKS")"
  if [ ! -f "$BOOT_TASKS" ]; then
    cat > "$BOOT_TASKS" <<'EOF'
# 开机自启任务配置：每行一个命令，# 开头为注释
# 路径含空格时用引号包裹，例如：
# "/root/apps/脚本/示例.sh"
# 常驻脚本请在行尾加 & 后台运行，避免阻塞后续任务
EOF
  fi
  systemctl daemon-reload
  systemctl enable boot-exec.service >/dev/null 2>&1 || true
  log "32;1" "开机任务框架已安装（tasks.list: $BOOT_TASKS）"
}

# ---------------- SSH 登录提醒 ----------------
setup_ssh_notify() {
  local prompt="是否添加 SSH 登录提醒脚本"
  if [ -f "$SSH_NOTIFY" ]; then
    prompt="SSH 登录提醒已安装，是否重新安装/更新"
  fi
  if ! ask_yn "$prompt" n; then
    log "32;1" "跳过 SSH 登录提醒"
    return 0
  fi
  download_or_die \
    "https://raw.githubusercontent.com/xct258/common/refs/heads/main/SSH%E7%99%BB%E5%BD%95%E6%8F%90%E9%86%92/ssh%E7%99%BB%E5%BD%95%E6%8F%90%E9%86%92.sh" \
    "$WORK_DIR/ssh_login_notify.sh"
  install -m 0644 "$WORK_DIR/ssh_login_notify.sh" "$SSH_NOTIFY"
  log "32;1" "SSH 登录提醒已安装"
}

# ---------------- OCI 附加 IP ----------------
report_ipv4_egress() {
  # report_ipv4_egress <cidr>...
  local cidr ip egress
  for cidr in "$@"; do
    [ -n "$cidr" ] || continue
    ip="${cidr%/*}"
    egress=$(curl -4 -s --max-time 8 --interface "$ip" https://api.ipify.org 2>/dev/null || true)
    if [ -z "$egress" ]; then
      egress=$(curl -4 -s --max-time 8 --interface "$ip" https://ifconfig.me 2>/dev/null || true)
    fi
    if [ -n "$egress" ]; then
      log "32;1" "附加 IPv4 $ip 的出口公网 IP：$egress"
    else
      log "33;1" "附加 IPv4 $ip 出口检测失败（云上未分配、未生效或被安全列表拦截）"
    fi
  done
}

oci_metadata_ok() {
  curl -sf --max-time 3 -H 'Authorization: Bearer Oracle' \
    http://169.254.169.254/opc/v2/instance/ >/dev/null 2>&1
}

configure_oci_ips() {
  if ! oci_metadata_ok; then
    log "32;1" "未检测到 OCI 元数据，跳过附加 IP 配置"
    return 0
  fi

  local vnics vnic_count v6_json v6_count
  vnics=$(curl -sf --max-time 5 -H 'Authorization: Bearer Oracle' \
    http://169.254.169.254/opc/v2/vnics/ 2>/dev/null || true)
  vnic_count=$(printf '%s' "$vnics" | tr -d ' \n' | grep -o '"vnicId"' | wc -l)
  if [ "${vnic_count:-0}" -gt 1 ]; then
    log "33;1" "检测到 $vnic_count 个 VNIC，脚本只处理第一个"
  fi
  v6_json=$(printf '%s' "$vnics" | tr -d ' \n' \
    | grep -o '"ipv6Addresses":\[[^]]*\]' | head -1 || true)
  v6_count=$(printf '%s' "$v6_json" | sed 's/^"ipv6Addresses"://' \
    | grep -o '"[^"]*"' | wc -l)

  # 已有配置时先询问，避免每次重跑都覆盖
  local old_v4="no"
  local -a old_ip4=()
  if [ -f "$OCI_CONF" ]; then
    log "33;1" "当前 OCI 附加 IP 配置："
    sed 's/^/  /' "$OCI_CONF"
    # 先解析旧配置，保留/重配两条路径都要用到
    # shellcheck source=/dev/null
    old_v4=$(. "$OCI_CONF" 2>/dev/null && echo "${ENABLE_IPV4:-no}")
    # shellcheck source=/dev/null
    mapfile -t old_ip4 < <(. "$OCI_CONF" 2>/dev/null \
      && declare -p IPV4_ADDRESSES >/dev/null 2>&1 \
      && printf '%s\n' "${IPV4_ADDRESSES[@]}" | grep -v '^$')
    if ! ask_yn "是否重新配置附加 IP" n; then
      log "32;1" "保留现有附加 IP 配置"
      if [ "${#old_ip4[@]}" -gt 0 ]; then
        report_ipv4_egress "${old_ip4[@]}"
      fi
      return 0
    fi
  fi

  local enable_v6=no enable_v4=no answer
  local -a ip4_list=()

  if [ "${v6_count:-0}" -gt 0 ]; then
    if ask_yn "检测到 ${v6_count} 个 IPv6，是否自动附加" y; then
      enable_v6=yes
    fi
  else
    log "33;1" "该网卡未分配 IPv6"
  fi

  local v4_default="n"
  [ "$old_v4" = "yes" ] && v4_default="y"
  if ask_yn "是否配置附加 IPv4（secondary private IP）" "$v4_default"; then
    enable_v4=yes
    # 重新配置时保留旧地址，直接回车 + done 即可沿用
    ip4_list=("${old_ip4[@]}")
    if [ "${#ip4_list[@]}" -gt 0 ]; then
      log "33;1" "已有地址：${ip4_list[*]}（直接输入 done 保留）"
    fi
    log "33;1" "输入附加 IPv4（CIDR，如 10.0.0.41/24），输入 done 结束"
    while true; do
      answer=$(ask_value "IPv4")
      [ "$answer" = "done" ] && break
      [ -z "$answer" ] && continue
      if ! valid_ipv4_cidr "$answer"; then
        log "31;1" "格式错误，应为 a.b.c.d/mask"
        continue
      fi
      if printf '%s\n' "${ip4_list[@]}" | grep -qxF "$answer"; then
        log "33;1" "已存在：$answer"
        continue
      fi
      ip4_list+=("$answer")
      log "32;1" "已添加：$answer"
    done
  else
    # 选择不配置时保留旧值，而不是清空
    enable_v4="$old_v4"
    ip4_list=("${old_ip4[@]}")
    log "32;1" "保留原有 IPv4 配置（${#ip4_list[@]} 条）"
  fi

  install_oci_script

  {
    echo "# 由 $0 生成"
    echo "ENABLE_IPV6=$enable_v6"
    echo "ENABLE_IPV4=$enable_v4"
    if [ "${#ip4_list[@]}" -gt 0 ]; then
      echo "IPV4_ADDRESSES=("
      printf '  %q\n' "${ip4_list[@]}"
      echo ")"
    else
      echo "IPV4_ADDRESSES=()"
    fi
  } > "$OCI_CONF"

  cat > "$OCI_UNIT" <<'EOF'
[Unit]
Description=Configure OCI secondary IPv4/IPv6 addresses
After=network-online.target
Wants=network-online.target
ConditionPathExists=/usr/local/sbin/oci-secondary-ips

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/local/sbin/oci-secondary-ips

[Install]
WantedBy=multi-user.target
EOF

  systemctl daemon-reload
  systemctl enable --now oci-secondary-ips.service \
    || log "33;1" "警告：oci-secondary-ips.service 启用失败，请手动检查"
  log "32;1" "OCI 附加 IP 已生效，并设置为本机开机自动附加"

  if [ "$enable_v4" = "yes" ] && [ "${#ip4_list[@]}" -gt 0 ]; then
    sleep 1
    report_ipv4_egress "${ip4_list[@]}"
  fi
}

install_oci_script() {
  cat > "$OCI_SCRIPT" <<'EOF'
#!/usr/bin/env bash
# OCI 附加 IP 配置：IPv6 取自实例元数据，IPv4 来自配置文件
# 幂等，可每次开机执行
set -u

CONF=/etc/oci-secondary-ips.conf
META_URL=http://169.254.169.254/opc/v2/vnics/
META_HDR='Authorization: Bearer Oracle'

ENABLE_IPV6=yes
ENABLE_IPV4=yes
IPV4_ADDRESSES=()
[ -r "$CONF" ] && . "$CONF"

log() { echo "oci-secondary-ips: $*"; }

IFACE=${IFACE:-$(ip -o -4 route show default 2>/dev/null | awk '{print $5; exit}')}
[ -z "${IFACE:-}" ] && IFACE=$(ip -o link show | awk -F': ' '$2 != "lo" {print $2; exit}')
[ -z "${IFACE:-}" ] && { log "no network interface found"; exit 1; }

if [ "$ENABLE_IPV6" = "yes" ]; then
  command -v pgrep >/dev/null 2>&1 && pgrep -x dhcpcd >/dev/null 2>&1 \
    && log "note: dhcpcd detected, /64 prefix may be overridden"
  json=$(curl -sf --max-time 5 -H "$META_HDR" "$META_URL" 2>/dev/null) || json=""
  flat=$(printf '%s' "$json" | tr -d ' \n\r\t')
  v6addrs=()
  if [ -n "$flat" ]; then
    while IFS= read -r addr; do
      [ -n "$addr" ] && v6addrs+=("$addr")
    done < <(printf '%s' "$flat" \
      | grep -o '"ipv6Addresses":\[[^]]*\]' | head -1 \
      | sed 's/^"ipv6Addresses"://' | grep -o '"[^"]*"' | tr -d '"')
  fi
  i=0
  for addr in "${v6addrs[@]:-}"; do
    [ -z "$addr" ] && continue
    if [ "$i" -eq 0 ]; then
      ip -6 addr replace "$addr/64" dev "$IFACE" && log "ipv6 $addr/64 on $IFACE"
    else
      ip -6 addr replace "$addr/128" dev "$IFACE" && log "ipv6 $addr/128 on $IFACE"
    fi
    i=$((i + 1))
  done
  gw=""
  [ -n "$flat" ] && gw=$(printf '%s' "$flat" \
    | grep -o '"ipv6VirtualRouterIp":"[^"]*"' | head -1 | cut -d'"' -f4)
  if [ -n "${gw:-}" ]; then
    ip -6 route replace default via "$gw" dev "$IFACE" 2>/dev/null \
      && log "ipv6 default via $gw"
  fi
fi

if [ "$ENABLE_IPV4" = "yes" ] && [ "${#IPV4_ADDRESSES[@]}" -gt 0 ]; then
  for ip4 in "${IPV4_ADDRESSES[@]}"; do
    [ -n "$ip4" ] || continue
    ip addr replace "$ip4" dev "$IFACE" && log "ipv4 $ip4 on $IFACE"
  done
fi

exit 0
EOF
  chmod 0755 "$OCI_SCRIPT"
}

# ---------------- 主流程 ----------------
main() {
  if [ "$(id -u)" -ne 0 ]; then
    echo "错误：必须以 root 用户运行" >&2
    exit 1
  fi
  mkdir -p "$(dirname "$LOG_FILE")"
  WORK_DIR=$(mktemp -d)
  trap cleanup EXIT

  if [ ! -t 0 ]; then
    log "33;1" "提示：标准输入不是终端，所有询问将使用默认值"
  fi

  log "33;1" ">>> 开始 Debian 服务器初始化（日志：$LOG_FILE）"

  setup_timezone
  apt-get update || log "33;1" "警告：apt update 失败，继续尝试"
  setup_locale
  install_base_packages
  setup_timesync
  clean_kernel_symlinks

  install_7zip
  maybe_install_docker
  setup_ufw
  install_boot_service
  setup_ssh_notify
  configure_oci_ips

  log "32;1" "执行成功"
}

main "$@"
