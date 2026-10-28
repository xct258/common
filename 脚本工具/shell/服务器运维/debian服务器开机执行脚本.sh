#!/bin/bash
# ================================================================
# Debian 服务器开机执行脚本（优化版）
#
# 基于原版优化，主要改进：
#   1. 开启 ufw 前自动检测并放行当前 SSH 端口，避免防火墙把远程连接拒之门外
#   2. 开机自启任务写入 tasks.list 时路径加引号，修复含空格路径静默失败的问题
#   3. BACKUP_PASSWORD 改用"删行+追加 + %q 转义"的安全写入，密码含 $/引号也不会被展开
#   4. boot-exec.service 增加 Wants=network-online.target，确保网络就绪后再执行
#   5. boot-exec.service 与常驻任务均以后台方式启动，避免 oneshot 被备份脚本长期阻塞
#   6. rclone.conf 位于私有仓库：支持 GITHUB_TOKEN/GH_TOKEN 环境变量或交互输入令牌（不回显），
#      带 Authorization 头走 GitHub API 下载；带令牌的请求不套国内加速，避免令牌泄露
#   7. 7zip 改为从 GitHub 官方仓库（ip7z/7zip）下载：官网 /a/ 直链已 404，
#      版本号经 GitHub API 实时查询，查不到直接退出（不使用固定版本），下载后校验可执行性
#   8. 增加 root 校验、set -u 安全处理、日志输出、EXIT 陷阱
#   9. 删除内核文件前校验为符号链接(-L)才删除，更保守
#  10. locale.gen 写入去重、备份目录去重、server_name/备份目录用 %q 写入可安全 source
#  11. Docker 安装改为「官方源重试 3 次 → 阿里云镜像 apt 源 → 仍失败则退出脚本」
#  12. 所有下载统一策略：直连 3 次（10 秒超时）→ GitHub 域名改走国内加速前缀 →
#      仍失败则退出脚本；rclone 官方脚本拿不到时回退国内 apt 源，仍失败退出
# ================================================================

set -euo pipefail

# ============ 基础准备 ============
# 必须是 root
if [ "$(id -u)" -ne 0 ]; then
  echo "错误：必须以 root 用户运行此脚本（sudo bash 该脚本）"
  exit 1
fi

# 日志
LOG_FILE="/var/log/boot-init.log"
mkdir -p "$(dirname "$LOG_FILE")"

# 输出到终端并同时写入日志
# 用法: log "<颜色>;<样式>" "文本"    例: log "33;1" "黄色加粗文本" ; log "" "普通文本"
log() {
  local style="$1"; shift
  echo "$*" >> "$LOG_FILE"
  if [ -n "$style" ]; then
    echo -e "\e[${style}m$*\e[0m"
  else
    echo "$*"
  fi
}

# 记录脚本最终退出码
trap 'rc=$?; log "" ">>> $(date "+%F %T") 脚本结束，退出码=$rc"' EXIT

# ============ 下载策略：直连 3 次 → 国内加速 → 仍失败退出 ============
# 国内加速前缀（仅用于 GitHub 域名），可用同名环境变量覆盖，空格分隔
GITHUB_MIRRORS="${GITHUB_MIRRORS:-https://ghfast.top/ https://gh-proxy.com/ https://ghproxy.net/}"

# 单次下载：curl 连接超时 10 秒，wget 总超时 10 秒
# 除 3 个必选参数外，其余参数按“额外请求头”传给工具（用于私有仓库令牌）
_dl_once() {
  local tool="$1" url="$2" out="$3"; shift 3
  local -a args=()
  local h
  if [ "$tool" = "curl" ]; then
    args=(curl -fsSL --connect-timeout 10 --max-time 120)
    for h in "$@"; do args+=(-H "$h"); done
    args+=(-o "$out" "$url")
  else
    args=(wget --timeout=10 --tries=1)
    for h in "$@"; do args+=(--header="$h"); done
    args+=(-O "$out" "$url")
  fi
  "${args[@]}"
}

# 判断是否 GitHub 域名（只有这类域名能套国内加速前缀）
_dl_is_github() {
  case "$1" in
    https://github.com/*|https://raw.githubusercontent.com/*|https://objects.githubusercontent.com/*|https://codeload.github.com/*)
      return 0 ;;
    *) return 1 ;;
  esac
}

# 下载：先直连 3 次，失败再逐个尝试国内加速前缀
# 成功返回 0，失败返回 1（不退出，便于调用方做额外兜底）
# 用法: download_try <curl|wget> <url> <输出路径> [额外请求头...]
# 带请求头（含令牌）时不做国内加速，避免令牌被第三方代理看到
download_try() {
  local tool="$1" url="$2" out="$3"; shift 3
  local -a headers=("$@")
  local attempt prefix tmp
  tmp="${out}.part.$$"
  for attempt in 1 2 3; do
    if _dl_once "$tool" "$url" "$tmp" "${headers[@]}"; then
      mv -f "$tmp" "$out"
      return 0
    fi
    log "33;1" "下载失败（第 $attempt/3 次，超时 10 秒）：$url"
    if [ "$attempt" -lt 3 ]; then
      sleep 3
    fi
  done
  if [ "${#headers[@]}" -eq 0 ] && _dl_is_github "$url"; then
    for prefix in $GITHUB_MIRRORS; do
      log "33;1" "直连失败，改用国内加速：${prefix}"
      if _dl_once "$tool" "${prefix}${url}" "$tmp"; then
        mv -f "$tmp" "$out"
        log "32;1" "国内加速下载成功：${prefix}"
        return 0
      fi
    done
  fi
  rm -f "$tmp"
  return 1
}

# 下载失败即退出脚本
# 用法: download_or_exit <curl|wget> <url> <输出路径> [额外请求头...]
download_or_exit() {
  local tool="$1" url="$2" out="$3"; shift 3
  if ! download_try "$tool" "$url" "$out" "$@"; then
    log "31;1" "错误：直连与国内加速均失败，退出脚本：$url"
    exit 1
  fi
}

# 解析私有仓库下载所需的 GitHub Token
# 优先取 GITHUB_TOKEN / GH_TOKEN 环境变量，否则交互输入（不回显），结果写入 GH_TOKEN_RESOLVED
resolve_github_token() {
  local token="${GITHUB_TOKEN:-${GH_TOKEN:-}}"
  if [ -z "$token" ] && [ -t 0 ]; then
    log "33;1" "rclone.conf 位于私有仓库，需要 GitHub Token（输入不回显，需该仓库 Contents 读权限）"
    read -rs -p "Token: " token || true
    echo
  fi
  if [ -z "$token" ]; then
    log "31;1" "错误：未提供 GitHub Token，无法下载私有仓库的 rclone.conf，退出脚本"
    exit 1
  fi
  GH_TOKEN_RESOLVED="$token"
}

# ============ 变量初始化（配合 set -u） ============
install_docker=""
install_rclone=""
overwrite_config=""
rclone_config=""
GH_TOKEN_RESOLVED=""
reset_name=""
server_name=""
skip_name_input=false
server_backup=""
install_ssh_push=""
server_free_date=""
skip_date=false
need_input=""
confirm_date=""
need_backup=""
backup_directories=()
skip_backup=false
directory=""
confirm=""
backup_password=""
old_dirs=()

# ============ 函数：安装开机自启服务 ============
install_boot_service() {
  # 1) 任务执行器
  cat > /usr/local/bin/boot-exec.sh <<-'EOF'
#!/bin/bash
# 开机自启任务执行器
# 逐行读取 /etc/boot-exec/tasks.list 并执行，# 开头或空行为注释
[ ! -f /etc/boot-exec/tasks.list ] && exit 0
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    ""|\#*) continue ;;
  esac
  echo "[boot-exec] 执行: $line"
  bash -c "$line" || echo "[boot-exec] 执行失败: $line"
done < /etc/boot-exec/tasks.list
EOF
  chmod +x /usr/local/bin/boot-exec.sh

  # 2) systemd 服务（网络就绪后再执行）
  cat > /etc/systemd/system/boot-exec.service <<-'EOF'
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
  systemctl daemon-reload
  systemctl enable boot-exec.service

  # 3) 任务列表模板
  mkdir -p /etc/boot-exec
  if [ ! -f /etc/boot-exec/tasks.list ]; then
    cat > /etc/boot-exec/tasks.list <<-'EOF'
# 开机自启任务配置
# 每行一个命令，按顺序执行，# 开头的行为注释
# 路径含空格时请用引号包裹，例如：
# "/root/apps/脚本/服务器备份脚本.sh"
# 常驻脚本（如备份监控）请在末尾加 & 后台运行，否则会阻塞后续任务：
# "/root/apps/脚本/服务器备份脚本.sh" &
# docker start nginx
EOF
  fi
}

# ============ 系统基础配置 ============
log "33;1" ">>> 开始执行 Debian 服务器初始化（日志: $LOG_FILE）"
if ! apt update; then
  log "33;1" "警告：apt update 失败（可能是网络问题），继续尝试安装"
fi

# 时区
timedatectl set-timezone Asia/Shanghai

# 中文环境（两阶段：非中文时配置后退出，重连 SSH 后重跑）
apt install -y locales
if locale | grep -q "zh_CN.UTF-8"; then
  log "32;1" "当前环境为中文，跳过安装中文语言环境"
else
  log "33;1" "当前环境不是中文，即将开始安装中文语言环境"
  sleep 3
  # 写入 locale.gen 前先去重，避免重复行
  grep -qxF "zh_CN.UTF-8 UTF-8" /etc/locale.gen 2>/dev/null || echo "zh_CN.UTF-8 UTF-8" >> /etc/locale.gen
  locale-gen zh_CN.UTF-8
  update-locale LANG=zh_CN.UTF-8
  log "31;1" "中文语言环境设置成功，请重新连接SSH，不要使用exit命令退出！"
  exit 1
fi

# 安装基础软件
apt install -y sudo wget nano ufw curl jq bc xz-utils systemd-timesyncd isc-dhcp-client

# 启用时间同步
systemctl enable systemd-timesyncd
systemctl start systemd-timesyncd

# ============ 清理不需要的内核符号链接 ============
rm_files=("/initrd.img" "/initrd.img.old" "/vmlinuz" "/vmlinuz.old")
for rm_file in "${rm_files[@]}"; do
  if [ -L "$rm_file" ]; then  # 仅删除符号链接，避免误删真实文件
    log "32;1" "删除不必要的符号链接 $rm_file"
    rm "$rm_file"
  fi
done

# ============ 安装 7zip ============
if [ -e "/bin/7zz" ]; then
  log "32;1" "7z已安装"
else
  # 官网 www.7-zip.org/a/ 直链已 404，改从 GitHub 官方仓库下载：
  # https://github.com/ip7z/7zip/releases
  # 版本号必须实时查询，查不到就退出，不使用固定回退版本
  latest_ver_7z=""
  for attempt in 1 2 3; do
    latest_ver_7z=$(curl -fsSL --connect-timeout 10 --max-time 20 \
      https://api.github.com/repos/ip7z/7zip/releases/latest 2>/dev/null \
      | grep -oP '"tag_name"\s*:\s*"\K[^"]+' | head -1 || true)
    if [ -n "$latest_ver_7z" ]; then
      break
    fi
    log "33;1" "获取 7zip 版本号失败（第 $attempt/3 次）"
    if [ "$attempt" -lt 3 ]; then
      sleep 3
    fi
  done
  if [ -z "$latest_ver_7z" ]; then
    log "31;1" "错误：无法获取 7zip 最新版本号（GitHub API 不可用），退出脚本"
    exit 1
  fi
  log "32;1" "7zip 最新版本：$latest_ver_7z"
  # 标签形如 26.05，资产文件名形如 7z2605-linux-x64.tar.xz
  ver_7z=$(printf '%s' "$latest_ver_7z" | tr -d '.')
  arch=$(uname -m)
  case "$arch" in
    *x86_64*)  pkg_7z="7z${ver_7z}-linux-x64.tar.xz" ;;
    *aarch64*) pkg_7z="7z${ver_7z}-linux-arm64.tar.xz" ;;
    *) log "31;1" "无法确定服务器架构（$arch），请手动安装7z"; exit 1 ;;
  esac
  download_url="https://github.com/ip7z/7zip/releases/download/${latest_ver_7z}/${pkg_7z}"
  log "33;1" "下载 7zip: $download_url"
  download_or_exit wget "$download_url" /tmp/7zz.tar.xz
  rm -rf /tmp/7zz
  if ! mkdir -p /tmp/7zz || ! tar -xf /tmp/7zz.tar.xz -C /tmp/7zz; then
    rm -rf /tmp/7zz /tmp/7zz.tar.xz
    log "31;1" "7z 解压失败"
    exit 1
  fi
  chmod +x /tmp/7zz/7zz
  # 运行一次确认二进制与当前系统兼容，避免装上一个跑不起来的 7zz
  if ! /tmp/7zz/7zz i >/dev/null 2>&1; then
    rm -rf /tmp/7zz /tmp/7zz.tar.xz
    log "31;1" "7z 可执行文件校验失败，请检查系统兼容性或改用 apt install p7zip-full"
    exit 1
  fi
  mv /tmp/7zz/7zz /bin/7zz
  rm -rf /tmp/7zz /tmp/7zz.tar.xz
  log "32;1" "7zip安装成功（版本 $latest_ver_7z）"
fi

# ============ 安装 Docker（可选） ============
# 官方源安装：下载失败或脚本执行失败都算本轮失败，最多重试 3 次
install_docker_official() {
  local attempt
  for attempt in 1 2 3; do
    log "33;1" "官方源安装 Docker（第 $attempt/3 次尝试）"
    if curl -fsSL --connect-timeout 10 --max-time 120 https://get.docker.com -o /tmp/get-docker.sh; then
      if bash /tmp/get-docker.sh; then
        rm -f /tmp/get-docker.sh
        log "32;1" "官方源安装 Docker 成功"
        return 0
      fi
    fi
    sleep 3
  done
  rm -f /tmp/get-docker.sh
  return 1
}

# 阿里云镜像安装：直接走 apt 源，不依赖 get.docker.com 是否可达
install_docker_aliyun() {
  local distro codename arch list key
  log "33;1" "官方源失败，回退阿里云镜像安装 Docker"
  distro=$(. /etc/os-release && echo "${ID:-debian}") || distro=debian
  codename=$(. /etc/os-release && echo "${VERSION_CODENAME:-bookworm}") || codename=bookworm
  [ "$distro" = "ubuntu" ] || distro=debian
  arch=$(dpkg --print-architecture) || return 1
  list="/etc/apt/sources.list.d/docker.list"
  key="/etc/apt/keyrings/docker.asc"

  apt-get update || return 1
  apt-get install -y ca-certificates curl gnupg || return 1
  install -m 0755 -d /etc/apt/keyrings || return 1
  if ! curl -fsSL --connect-timeout 10 --max-time 60 \
      "https://mirrors.aliyun.com/docker-ce/linux/${distro}/gpg" -o "$key"; then
    return 1
  fi
  chmod a+r "$key"

  # 写入源之后任何一步失败都要删掉源文件，否则下次 apt-get update 会一直报错
  if ! printf 'deb [arch=%s signed-by=%s] https://mirrors.aliyun.com/docker-ce/linux/%s %s stable\n' \
      "$arch" "$key" "$distro" "$codename" > "$list"; then
    rm -f "$list"
    return 1
  fi
  if ! apt-get update; then
    rm -f "$list"
    return 1
  fi
  if ! apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin; then
    rm -f "$list"
    return 1
  fi
  systemctl enable --now docker >/dev/null 2>&1 || true
  log "32;1" "阿里云镜像安装 Docker 成功"
  return 0
}

if command -v docker &> /dev/null; then
  log "32;1" "docker已安装，无需重复安装"
else
  while true; do
    log "33;1" "docker未安装，是否要安装docker？[y/n]: "
    read -r -e install_docker
    case "$install_docker" in
      y|Y)
        # 直连重试 3 次 → 回退阿里云镜像 → 都失败则退出脚本
        if ! install_docker_official && ! install_docker_aliyun; then
          log "31;1" "错误：官方源与阿里云镜像均安装失败，请检查网络后手动安装 Docker"
          exit 1
        fi
        if command -v docker &> /dev/null; then
          log "32;1" "验证成功：Docker 安装成功！当前版本如下："
          docker --version
        else
          log "31;1" "错误：安装流程已执行，但系统中未检测到 docker 命令，安装失败"
          exit 1
        fi
        break
        ;;
      n|N)
        log "32;1" "不安装docker"
        break
        ;;
      *)
        log "31;1" "无效输入，请输入 'y' 或 'n'"
        ;;
    esac
  done
fi

# ============ 防火墙 ufw（自动放行当前 SSH 端口） ============
ufw_status=$(ufw status 2>/dev/null || true)
if [[ $ufw_status == *"Status: active"* ]]; then
  log "32;1" "ufw已启用，无需再次启动"
else
  # 检测当前 SSH 监听端口，避免开启防火墙后把自己挡在外面
  ssh_port=""
  if command -v ss &> /dev/null; then
    ssh_port=$(ss -tnlp 2>/dev/null | awk '/sshd/ {port=$4; sub(/.*:/,"",port); print port}' | sort -un | head -1 || true)
  fi
  [ -z "$ssh_port" ] && ssh_port="22"
  log "33;1" "检测到 SSH 端口: $ssh_port，将放行该端口"
  ufw allow "$ssh_port"/tcp
  ufw allow ssh
  ufw allow 80
  ufw allow 443
  log "33;1" "SSH($ssh_port) 80 443 端口已打开，正在开启防火墙"
  ufw --force enable
  log "32;1" "ufw已启用"
fi

# ============ 安装 rclone（可选，配置直接下载，无需 token） ============
if command -v rclone &> /dev/null; then
  log "32;1" "rclone已安装，无需再次执行"
else
  while true; do
    log "33;1" "rclone未安装，是否要安装rclone？[y/n]: "
    read -r -e install_rclone
    case "$install_rclone" in
      y|Y)
        # 直连 3 次 → 国内加速（GitHub 前缀 / Debian apt 源）→ 都失败退出
        if download_try curl https://rclone.org/install.sh /tmp/rclone-install.sh; then
          if ! bash /tmp/rclone-install.sh; then
            rm -f /tmp/rclone-install.sh
            log "31;1" "错误：rclone 安装脚本执行失败，退出脚本"
            exit 1
          fi
          rm -f /tmp/rclone-install.sh
        else
          log "33;1" "官方安装脚本不可用，改用国内 apt 源安装 rclone"
          apt-get update >/dev/null 2>&1 || true
          if ! apt-get install -y rclone; then
            log "31;1" "错误：rclone 安装失败（官方脚本与国内 apt 源均失败），退出脚本"
            exit 1
          fi
        fi
        if ! command -v rclone &> /dev/null; then
          log "31;1" "错误：安装流程已执行，但未检测到 rclone 命令，退出脚本"
          exit 1
        fi
        log "32;1" "rclone已安装"
        CONFIG_FILE_rclone="/root/.config/rclone/rclone.conf"
        if [ -f "$CONFIG_FILE_rclone" ]; then
          while true; do
            log "33;1" "rclone配置文件已存在，是否覆盖？[y/n]: "
            read -r -e overwrite_choice
            case "$overwrite_choice" in
              y|Y) overwrite_config="y"; break ;;
              n|N) overwrite_config=""; log "32;1" "不覆盖配置文件"; break ;;
              *) log "31;1" "无效输入，请输入 'y' 或 'n'" ;;
            esac
          done
        else
          overwrite_config="y"
        fi
        if [ "$overwrite_config" = "y" ]; then
          while true; do
            log "33;1" "是否需要自动加载rclone配置文件？[y/n]: "
            read -r -e rclone_config
            case "$rclone_config" in
              y|Y)
                mkdir -p /root/.config/rclone
                resolve_github_token
                # 私有仓库：用 GitHub API 取原始内容，带令牌头；带令牌时不会走国内加速代理
                download_or_exit wget \
                  "https://api.github.com/repos/xct258/Documentation/contents/rclone/rclone.conf?ref=main" \
                  "$CONFIG_FILE_rclone" \
                  "Authorization: Bearer ${GH_TOKEN_RESOLVED}" \
                  "Accept: application/vnd.github.raw"
                if [ ! -s "$CONFIG_FILE_rclone" ]; then
                  log "31;1" "错误：rclone.conf 下载内容为空，请检查令牌权限是否包含该仓库 Contents 读取，退出脚本"
                  exit 1
                fi
                log "32;1" "rclone配置文件已加载"
                break
                ;;
              n|N)
                log "32;1" "不自动配置"
                break
                ;;
              *)
                log "31;1" "无效输入，请输入 'y' 或 'n'"
                ;;
            esac
          done
        fi
        break
        ;;
      n|N)
        log "32;1" "不安装rclone"
        break
        ;;
      *)
        log "31;1" "无效输入，请输入 'y' 或 'n'"
        ;;
    esac
  done
fi

# ============ 安装开机自启服务 ============
if [ ! -f /etc/boot-exec/tasks.list ]; then
  log "33;1" "安装开机自启服务"
  install_boot_service
else
  log "33;1" "开机自启服务已存在，跳过安装"
fi

# ============ 服务器名称 ============
CONFIG_FILE="/root/apps/脚本/config/服务器基本信息.txt"
old_name=""
if [ -f "$CONFIG_FILE" ] && grep -q "^server_name=" "$CONFIG_FILE" 2>/dev/null; then
  # 通过 source 读取，兼容含空格/特殊字符的已引号值
  old_name=$( . "$CONFIG_FILE" >/dev/null 2>&1; printf '%s' "${server_name:-}" )
  if [ -z "$old_name" ]; then
    log "31;1" "配置文件中的 server_name 无法解析，将重新输入"
  else
    log "33;1" "当前服务器名称: $old_name"
    log "33;1" "是否重新设置？[y/n]: "
    read -r -e reset_name
    if [ "$reset_name" != "y" ] && [ "$reset_name" != "Y" ]; then
      server_name="$old_name"
      log "32;1" "使用现有服务器名称: $server_name"
      skip_name_input=true
    fi
  fi
fi

if [ "$skip_name_input" != "true" ]; then
  while true; do
    log "32;1" "输入服务器名称"
    log "33;1" "示例：甲骨文-1-debian-1"
    read -r -e server_name
    if [ -z "$server_name" ]; then
      log "31;1" "服务器名称不能为空，请重新输入"
      continue
    fi
    log "32;1" "您输入的服务器名称是："
    log "32;1" "$server_name"
    log "33;1" "是否确认服务器名称？[y/n]: "
    read -r -e input_name_sever
    if [ "$input_name_sever" = "y" ] || [ "$input_name_sever" = "Y" ]; then
      mkdir -p "/root/apps/脚本/config"
      # 只替换 server_name 行，保留已有的备份目录/密码等其他配置
      if [ -f "$CONFIG_FILE" ]; then
        sed -i '/^server_name=/d' "$CONFIG_FILE" 2>/dev/null || true
      fi
      printf 'server_name=%q\n' "$server_name" >> "$CONFIG_FILE"
      break
    fi
  done
fi

# ============ 备份/监控脚本 ============
log "33;1" "是否需要自动加载备份/监控脚本？[y/n]: "
read -r -e server_backup
while [[ ! $server_backup =~ ^[YyNn]$ ]]; do
  log "33;1" "输入无效，请输入[y/n]："
  read -r -e server_backup
done

if [[ ! $server_backup =~ ^[Yy]$ ]]; then
  # —— 不加载备份脚本 ——
  log "32;1" "不自动配置脚本"
  log "33;1" "是否添加用户登陆ssh推送消息脚本？[y/n]: "
  read -r -e install_ssh_push
  while [[ ! $install_ssh_push =~ ^[YyNn]$ ]]; do
    log "33;1" "输入无效，请输入[y/n]："
    read -r -e install_ssh_push
  done
  if [[ ! $install_ssh_push =~ ^[Yy]$ ]]; then
    log "32;1" "不添加ssh推送消息脚本"
  else
    download_or_exit wget "https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/ssh登录提醒.sh" /etc/profile.d/ssh_login_notify.sh
    chmod +x /etc/profile.d/ssh_login_notify.sh
    log "32;1" "用户登陆ssh推送消息配置完成"
  fi
else
  # —— 加载备份脚本 ——
  mkdir -p "/root/apps/脚本"
  download_or_exit wget "https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/服务器备份监控脚本.sh" "/root/apps/脚本/$server_name.sh"
  chmod +x "/root/apps/脚本/$server_name.sh"
  log "32;1" "备份脚本已下载到 /root/apps/脚本/$server_name.sh"

  # —— 可用时间（三层校验：格式 / 有效性 / 必须晚于今天） ——
  if grep -q "^server_free_datetime=" "$CONFIG_FILE" 2>/dev/null; then
    old_date=$(grep "^server_free_datetime=" "$CONFIG_FILE" | head -1 | cut -d= -f2-)
    if [ -n "$old_date" ]; then
      log "33;1" "当前可用时间: $old_date"
      log "33;1" "是否重新设置？[y/n]："
      read -r -e reset_date
      if [ "$reset_date" != "y" ] && [ "$reset_date" != "Y" ]; then
        server_free_date="$old_date"
        skip_date=true
      fi
    fi
  fi
  if [ "$skip_date" != "true" ]; then
    while true; do
      log "33;1" "是否需要输入${server_name}的可用时间？[y/n]："
      read -r -e need_input
      while [[ ! $need_input =~ ^[YyNn]$ ]]; do
        log "33;1" "输入无效，请输入[y/n]："
        read -r -e need_input
      done
      if [[ ! $need_input =~ ^[Yy]$ ]]; then
        log "32;1" "您选择不输入可用时间，将跳过该步骤。"
        break
      fi
      while true; do
        log "33;1" "请输入${server_name}可用时间（格式：YYYY-MM-DD）："
        read -r -e server_free_date
        if [[ ! $server_free_date =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
          log "31;1" "格式错误，请重新输入（例如 2025-10-07）。"
          continue
        fi
        valid_date=$(date -d "$server_free_date" "+%Y-%m-%d" 2>/dev/null || true)
        if [[ "$valid_date" != "$server_free_date" ]]; then
          log "31;1" "日期无效，请重新输入。"
          continue
        fi
        today=$(date "+%Y-%m-%d")
        if [[ ! "$server_free_date" > "$today" ]]; then
          log "31;1" "可用时间必须大于今天($today)，请重新输入。"
          continue
        fi
        break
      done
      log "32;1" "您输入的日期为：$server_free_date"
      log "33;1" "是否确认？[y/n]："
      read -r -e confirm_date
      while [[ ! $confirm_date =~ ^[YyNn]$ ]]; do
        log "33;1" "输入无效，请输入[y/n]："
        read -r -e confirm_date
      done
      if [ "$confirm_date" = "y" ] || [ "$confirm_date" = "Y" ]; then
        break
      fi
    done
  fi

  # 写入可用时间（日期已校验为安全字符，可安全用 sed 替换）
  if [ -n "$server_free_date" ]; then
    if grep -q "^server_free_datetime=" "$CONFIG_FILE" 2>/dev/null; then
      sed -i "s/^server_free_datetime=.*/server_free_datetime=$server_free_date/" "$CONFIG_FILE"
    else
      echo "server_free_datetime=$server_free_date" >> "$CONFIG_FILE"
    fi
  fi

  # —— 备份目录 ——
  if grep -q "^backup_directories=(" "$CONFIG_FILE" 2>/dev/null; then
    # 通过 source 读取，兼容 "带引号" 与 %q 转义两种写法
    mapfile -t old_dirs < <(
      . "$CONFIG_FILE" >/dev/null 2>&1
      if declare -p backup_directories >/dev/null 2>&1 && [ ${#backup_directories[@]} -gt 0 ]; then
        for d in "${backup_directories[@]}"; do printf '%s\n' "$d"; done
      fi
    )
    if [ ${#old_dirs[@]} -gt 0 ]; then
      log "33;1" "当前备份目录:"
      for dir in "${old_dirs[@]}"; do
        log "32;1" "- $dir"
      done
      log "33;1" "是否重新设置？[y/n]："
      read -r -e reset_backup
      if [ "$reset_backup" != "y" ] && [ "$reset_backup" != "Y" ]; then
        backup_directories=("${old_dirs[@]}")
        skip_backup=true
      fi
    fi
  fi

  # 去重添加函数
  add_directory() {
    local d="$1"
    for existing in "${backup_directories[@]}"; do
      [ "$existing" = "$d" ] && return 0
    done
    backup_directories+=("$d")
  }

  if [ "$skip_backup" != "true" ]; then
    log "33;1" "是否需要备份服务器上的目录？[y/n]："
    read -r -e need_backup
    while [[ ! $need_backup =~ ^[YyNn]$ ]]; do
      log "33;1" "输入无效，请输入[y/n]："
      read -r -e need_backup
    done
    if [[ ! $need_backup =~ ^[Yy]$ ]]; then
      log "32;1" "不备份服务器上的目录"
    else
      add_directory "/home/xct258/docker"
      add_directory "/home/xct258/apps"
      log "34;1" "默认已添加以下目录："
      for dir in "${backup_directories[@]}"; do
        log "32;1" "- $dir"
      done
      while true; do
        log "33;1" "请输入要备份的目录路径（如果不需要继续添加，请输入 'done'）："
        if [ ${#backup_directories[@]} -gt 0 ]; then
          log "34;1" "当前已添加的目录："
          for dir in "${backup_directories[@]}"; do
            log "32;1" "- $dir"
          done
          log "33;1" "可以继续添加，输入 'done' 完成添加"
        fi
        read -r -e directory
        if [ "$directory" = "done" ]; then
          break
        fi
        if [ -z "$directory" ]; then
          log "31;1" "目录路径不能为空"
          continue
        fi
        log "33;1" "您输入的目录路径为："
        log "32;1" "$directory"
        log "33;1" " 确认添加该目录吗？[y/n]："
        read -r -e confirm
        while [[ ! $confirm =~ ^[YyNn]$ ]]; do
          log "33;1" "输入无效，请输入[y/n]："
          read -r -e confirm
        done
        if [[ $confirm =~ ^[Yy]$ ]]; then
          add_directory "$directory"
        else
          log "31;1" "取消添加目录：$directory"
        fi
      done
    fi
  fi

  # 写入备份目录
  if [ ${#backup_directories[@]} -gt 0 ]; then
    if grep -q "^backup_directories=(" "$CONFIG_FILE" 2>/dev/null; then
      sed -i '/^backup_directories=(/,/^)/d' "$CONFIG_FILE"
    fi
    {
      echo "backup_directories=("
      for dir in "${backup_directories[@]}"; do
        printf '  %q\n' "$dir"
      done
      echo ")"
    } >> "$CONFIG_FILE"
    log "32;1" "备份目录已保存到 $CONFIG_FILE"
  fi

  # —— 7z 加密密码（安全写入，避免 sed 特殊字符问题） ——
  log "33;1" "请输入 7z 加密密码（直接回车使用默认密码）："
  read -r -e backup_password
  if [ -n "$backup_password" ]; then
    sed -i '/^BACKUP_PASSWORD=/d' "$CONFIG_FILE" 2>/dev/null || true
    # 用 %q 转义，密码含 $、引号、反斜杠时 source 也不会被展开或截断
    printf 'BACKUP_PASSWORD=%q\n' "$backup_password" >> "$CONFIG_FILE"
  fi

  # —— 注册到开机自启（路径加引号，兼容含空格名称；末尾 & 后台运行，避免阻塞后续任务） ——
  task_line="/root/apps/脚本/$server_name.sh"
  quoted_task=$(printf '%q' "$task_line")
  bg_task="$quoted_task &"
  if ! grep -qxF "$bg_task" /etc/boot-exec/tasks.list 2>/dev/null \
     && ! grep -qxF "$quoted_task" /etc/boot-exec/tasks.list 2>/dev/null \
     && ! grep -qxF "$task_line" /etc/boot-exec/tasks.list 2>/dev/null; then
    echo "$bg_task" >> /etc/boot-exec/tasks.list
  fi

  # —— SSH 登录提醒 ——
  if [ -f "/etc/profile.d/ssh_login_notify.sh" ]; then
    log "32;1" "删除ssh_login_notify.sh文件，重新创建"
    rm /etc/profile.d/ssh_login_notify.sh
  fi
  download_or_exit wget "https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/ssh登录提醒.sh" /etc/profile.d/ssh_login_notify.sh
  chmod +x /etc/profile.d/ssh_login_notify.sh
  log "32;1" "用户登陆ssh推送消息配置完成"

  # —— 立即执行一次开机自启任务（后台运行，不阻塞本脚本） ——
  if [ -f /usr/local/bin/boot-exec.sh ]; then
    log "33;1" "立即执行一次开机自启任务（后台）"
    nohup /usr/local/bin/boot-exec.sh >/dev/null 2>&1 &
  fi
fi

log "32;1" "执行成功"
