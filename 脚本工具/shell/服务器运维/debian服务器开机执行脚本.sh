#!/bin/bash

# 设置在发生错误时立即退出脚本
set -e

# 定义一个函数用于输出带颜色和样式的文本
# 示例：
# color_text "31" "1" "这是红色文本"
# color_text "32" "1" "这是绿色文本"
# color_text "33" "1" "这是黄色文本"
# color_text "34" "1" "这是蓝色文本"
color_text() {
  local color_1 style_1 text_1
  color_1=$1
  style_1=$2
  text_1=$3
  echo -e "\e[${style_1};${color_1}m${text_1}\e[0m"
}

install_boot_service() {
  cat > /usr/local/bin/boot-exec.sh <<- 'EOF'
	#!/bin/bash
	[ ! -f /etc/boot-exec/tasks.list ] && exit 0
	while IFS= read -r -e line || [ -n "$line" ]; do
	  [ -z "$line" ] || [ "${line#\#}" != "$line" ] && continue
	  bash -c "$line" >/dev/null 2>&1
	done < /etc/boot-exec/tasks.list
	EOF
  chmod +x /usr/local/bin/boot-exec.sh

  cat > /etc/systemd/system/boot-exec.service <<- 'EOF'
	[Unit]
	Description=boot-exec
	After=network-online.target

	[Service]
	Type=oneshot
	RemainAfterExit=yes
	ExecStart=/bin/bash -c "/usr/local/bin/boot-exec.sh &"

	[Install]
	WantedBy=multi-user.target
	EOF
  systemctl daemon-reload && systemctl enable boot-exec.service

  mkdir -p /etc/boot-exec
  if [ ! -f /etc/boot-exec/tasks.list ]; then
    cat > /etc/boot-exec/tasks.list <<- 'EOF'
	# 开机自启任务配置
	# 每行一个命令，按顺序执行，# 开头的行为注释
	# 示例：
	# /root/apps/脚本/服务器备份脚本.sh
	# docker start nginx
	EOF
  fi
}

get_github_token() {
  color_text 33 1 "请输入 GitHub 的令牌"
  read -r -e github_token
  if [ -z "$github_token" ]; then
    color_text 31 1 "令牌为空"
    return 1
  fi
  color_text 33 1 "您输入的 GitHub 令牌是: "
  color_text 32 1 "$github_token"
  color_text 33 1 "请确认输入的令牌是否正确[y/n]: "
  read -r -e confirmation
  if [ "$confirmation" != "y" ]; then
    color_text 31 1 "令牌未确认"
    return 1
  fi
}

apt update

# 设置时区为上海
timedatectl set-timezone Asia/Shanghai

# 设置中文环境
apt install -y locales
# 检查当前环境是否为中文
if locale | grep -q "zh_CN.UTF-8"; then
  color_text 32 1 "当前环境为中文，跳过安装中文语言环境"
else
  color_text 33 1 "当前环境不是中文，即将开始安装中文语言环境"
  sleep 3
  # 确保语言环境文件存在
  echo "zh_CN.UTF-8 UTF-8" >> /etc/locale.gen
  # 生成语言环境
  locale-gen zh_CN.UTF-8
  # 设置系统默认语言环境
  update-locale LANG=zh_CN.UTF-8
  color_text 31 1 "中文语言环境设置成功，请重新连接SSH，不要使用exit命令退出！"
  exit 1
fi

# 安装基础软件
apt install -y sudo wget nano ufw curl jq bc xz-utils systemd-timesyncd isc-dhcp-client

# 启用并启动 systemd-timesyncd时间同步
systemctl enable systemd-timesyncd
systemctl start systemd-timesyncd

# 删除不必要的文件
rm_files=("/initrd.img" "/initrd.img.old" "/vmlinuz" "/vmlinuz.old")
# 循环检查每个文件
for rm_file in "${rm_files[@]}"; do
    if [ -e "$rm_file" ]; then  # 检查文件是否存在
        color_text 32 1 "删除不必要的文件$rm_file"
        rm "$rm_file"  # 删除文件
    fi
done

# 安装7zip
if [ -e "/bin/7zz" ]; then
  color_text 32 1 "7z已安装"
else
  # 获取最新版本号
  latest_ver_7z=$(curl -s https://www.7-zip.org/download.html | grep -oP '7z\K[0-9]{4}' | head -1)
  # 使用 uname 命令获取服务器的架构信息，并使用 grep 过滤出包含 "x86_64" 或 "aarch64" 的行
  arch=$(uname -m)
  # 检查架构信息，根据结果下载不同架构的7zip（版本为2409，请注意更新最新的7zip下载链接）
  if [[ $arch == *"x86_64"* ]]; then
    download_url="https://www.7-zip.org/a/7z${latest_ver_7z}-linux-x64.tar.xz"
  elif [[ $arch == *"aarch64"* ]]; then
    download_url="https://www.7-zip.org/a/7z${latest_ver_7z}-linux-arm64.tar.xz"
  else
    color_text 31 1 "无法确定服务器架构（$arch），请手动安装7z"
    exit 1
  fi
  if ! wget -O 7zz.tar.xz "$download_url"; then
    color_text 31 1 "7z 下载失败"
    exit 1
  fi
  mkdir -p 7zz
  tar -xf 7zz.tar.xz -C 7zz
  chmod +x 7zz/7zz
  mv 7zz/7zz /bin/7zz
  rm -rf 7zz
  rm -rf 7zz.tar.xz
  color_text 32 1 "7zip安装成功"
fi

# 检测docker是否安装
if command -v docker &> /dev/null; then
  # docker已经存在，跳过安装docker
  color_text 32 1 "docker已安装，无需重复安装"
else
  # 安装Docker
  while true; do
    color_text 33 1 "docker未安装，是否要安装docker？[y/n]: "
    read -r -e install_docker
    if [ "$install_docker" == "y" ]; then
      # docker安装官方一键脚本
      curl -fsSL https://get.docker.com | bash -s docker
      
      # 【核心修改：安装后立即校验】
      if command -v docker &> /dev/null; then
        color_text 32 1 "验证成功：Docker 安装成功！当前版本如下："
        docker --version
      else
        color_text 31 1 "错误：Docker 脚本执行完毕，但系统中未检测到 docker 命令，可能安装失败。"
      fi
      color_text 32 1 "docker已安装"
      break  # 退出循环
    elif [ "$install_docker" == "n" ]; then
      color_text 32 1 "不安装docker"
      break  # 退出循环
    else
      color_text 31 1 "无效输入，请输入 'y' 或 'n'"
    fi
  done
fi

# 检测ufw防火墙是否启用
ufw_status=$(ufw status)
if [[ $ufw_status == *"Status: active"* ]]; then
  color_text 32 1 "ufw已启用，无需再次启动"
else
  # 配置防火墙规则
  ufw allow ssh
  ufw allow 80
  ufw allow 443
  color_text 33 1 "ssh 80 443端口已打开，正在开启防火墙"
  yes | ufw enable
  color_text 32 1 "ufw已启用"
fi

# 安装rclone
# 检测rclone是否存在
if command -v rclone &> /dev/null; then
  color_text 32 1 "rclone已安装，无需再次执行"
else
  # 询问用户是否安装rclone
  while true; do
    color_text 33 1 "rclone未安装，是否要安装rclone？[y/n]: "
    read -r -e install_rclone
    case "$install_rclone" in
      y)
        # 安装rclone网盘挂载服务
        curl https://rclone.org/install.sh | bash
        color_text 32 1 "rclone已安装"
        # 检查配置文件是否存在
        CONFIG_FILE_rclone="/root/.config/rclone/rclone.conf"
        if [ -f "$CONFIG_FILE_rclone" ]; then
          while true; do
            color_text 33 1 "rclone配置文件已存在，是否覆盖？[y/n]: "
            read -r -e overwrite_config
            case "$overwrite_config" in
              y)
                break
              ;;
              n)
                color_text 32 1 "不覆盖配置文件"
                overwrite_config=""
                break
              ;;
              *)
                color_text 31 1 "无效输入，请输入 'y' 或 'n'"
              ;;
            esac
          done
        else
          overwrite_config="y" # 如果配置文件不存在，默认覆盖
        fi
        # 询问是否加载配置文件
        if [ "$overwrite_config" != "" ]; then
          while true; do
            color_text 33 1 "是否需要自动加载rclone配置文件？[y/n]: "
            read -r -e rclone_config
            case "$rclone_config" in
              y)
                if get_github_token; then
                  mkdir -p /root/.config/rclone
                  wget --header="Authorization: token $github_token" -O "$CONFIG_FILE_rclone" https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/rclone/rclone.conf
                  color_text 32 1 "rclone配置文件已加载"
                  break
                fi
              ;;
              n)
                color_text 32 1 "不自动配置"
                break
              ;;
              *)
                color_text 31 1 "无效输入，请输入 'y' 或 'n'"
              ;;
            esac
          done
        fi
        break
        ;;
      n)
        color_text 32 1 "不安装rclone"
        break
        ;;
      *)
        color_text 31 1 "无效输入，请输入 'y' 或 'n'"
        ;;
    esac
  done
fi

# 安装开机自启服务
if [ ! -f /etc/boot-exec/tasks.list ]; then
  color_text 33 1 "安装开机自启服务"
  install_boot_service
else
  color_text 33 1 "开机自启服务存在，跳过安装"
fi

# 添加服务器备份/监控脚本
CONFIG_FILE="/root/apps/脚本/config/服务器基本信息.txt"
if [ -f "$CONFIG_FILE" ] && grep -q "^server_name=" "$CONFIG_FILE" 2>/dev/null; then
  old_name=$(grep "^server_name=" "$CONFIG_FILE" | cut -d= -f2)
  color_text 33 1 "当前服务器名称: $old_name"
  color_text 33 1 "是否重新设置？[y/n]: "
  read -r -e reset_name
  if [ "$reset_name" != "y" ]; then
    server_name="$old_name"
    color_text 32 1 "使用现有服务器名称: $server_name"
    skip_name_input=true
  fi
fi

if [ "$skip_name_input" != "true" ]; then
  while true; do
    color_text 32 1 "输入服务器名称"
    color_text 33 1 "示例：甲骨文-1-debian-1"
    read -r -e server_name
    color_text 32 1 "您输入的服务器名称是："
    color_text 32 1 "$server_name"
    color_text 33 1 "是否确认服务器名称？[y/n]: "
    read -r -e input_name_sever
    if [ "$input_name_sever" == "y" ]; then
      mkdir -p "/root/apps/脚本/config"
      echo server_name="$server_name" > "$CONFIG_FILE"
      break
    fi
  done
fi
color_text 33 1 "是否需要自动加载备份/监控脚本？[y/n]: "
read -r -e server_backup

# 验证用户输入
while [[ ! $server_backup =~ ^[YyNn]$ ]]; do
  color_text 33 1 "输入无效，请输入[y/n]："
  read -r -e server_backup
done
if [[ ! $server_backup =~ ^[Yy]$ ]]; then
  color_text 32 1 "不自动配置脚本"
  # 询问用户是否安装用户登陆ssh推送消息脚本
  color_text 33 1 "是否添加用户登陆ssh推送消息脚本？[y/n]: "
  read -r -e install_ssh_push

  # 验证用户输入
  while [[ ! $install_ssh_push =~ ^[YyNn]$ ]]; do
    color_text 33 1 "输入无效，请输入[y/n]："
    read -r -e install_ssh_push
  done
  if [[ ! $install_ssh_push =~ ^[Yy]$ ]]; then
    color_text 32 1 "不添加ssh推送消息脚本"
  else
    wget -O /etc/profile.d/ssh_login_notify.sh https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/ssh登录提醒.sh
    chmod +x /etc/profile.d/ssh_login_notify.sh
    color_text 32 1 "用户登陆ssh推送消息配置完成"
  fi
else
  # 下载脚本
  wget -O "$server_name".sh https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/服务器备份监控脚本.sh
  chmod +x "$server_name".sh
  mv "$server_name".sh "/root/apps/脚本/$server_name.sh"

  # 询问用户是否需要输入可用时间
  if grep -q "^server_free_datetime=" "$CONFIG_FILE" 2>/dev/null; then
    old_date=$(grep "^server_free_datetime=" "$CONFIG_FILE" | cut -d= -f2)
    if [ -n "$old_date" ]; then
      color_text 33 1 "当前可用时间: $old_date"
      color_text 33 1 "是否重新设置？[y/n]："
      read -r -e reset_date
      if [ "$reset_date" != "y" ]; then
        server_free_date="$old_date"
        skip_date=true
      fi
    fi
  fi
  if [ "$skip_date" != "true" ]; then
    while true; do
      color_text 33 1 "是否需要输入${server_name}的可用时间？[y/n]："
      read -r -e need_input
      while [[ ! $need_input =~ ^[YyNn]$ ]]; do
        color_text 33 1 "输入无效，请输入[y/n]："
        read -r -e need_input
      done
      if [[ ! $need_input =~ ^[Yy]$ ]]; then
        color_text 32 1 "您选择不输入可用时间，将跳过该步骤。"
        break
      fi
      while true; do
        color_text 33 1 "请输入${server_name}可用时间（格式：YYYY-MM-DD）："
        read -r -e server_free_date
        if [[ ! $server_free_date =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
          color_text 31 1 "格式错误，请重新输入（例如 2025-10-07）。"
          continue
        fi
        valid_date=$(date -d "$server_free_date" "+%Y-%m-%d" 2>/dev/null)
        if [[ "$valid_date" != "$server_free_date" ]]; then
          color_text 31 1 "日期无效，请重新输入。"
          continue
        fi
        today=$(date "+%Y-%m-%d")
        if [[ ! "$server_free_date" > "$today" ]]; then
          color_text 31 1 "可用时间必须大于今天($today)，请重新输入。"
          continue
        fi
        break
      done
      color_text 32 1 "您输入的日期为：$server_free_date"
      color_text 33 1 "是否确认？[y/n]："
      read -r -e confirm_date
      while [[ ! $confirm_date =~ ^[YyNn]$ ]]; do
        color_text 33 1 "输入无效，请输入[y/n]："
        read -r -e confirm_date
      done
      if [ "$confirm_date" == "y" ]; then
        break
      fi
    done
  fi

  # 将服务器可用时间写入文件
  if [ -n "$server_free_date" ]; then
    if grep -q "^server_free_datetime=" "$CONFIG_FILE" 2>/dev/null; then
      sed -i "s/^server_free_datetime=.*/server_free_datetime=$server_free_date/" "$CONFIG_FILE"
    else
      echo server_free_datetime="$server_free_date" >> "$CONFIG_FILE"
    fi
  fi

  # 询问用户是否需要备份服务器上的目录
  if grep -q "^backup_directories=(" "$CONFIG_FILE" 2>/dev/null; then
    mapfile -t old_dirs < <(sed -n '/^backup_directories=(/,/^)/{ /^backup_directories=(/d; /^)/d; s/^[[:space:]]*"\(.*\)"/\1/p }' "$CONFIG_FILE")
    if [ ${#old_dirs[@]} -gt 0 ]; then
      color_text 33 1 "当前备份目录:"
      for dir in "${old_dirs[@]}"; do
        color_text 32 1 "- $dir"
      done
      color_text 33 1 "是否重新设置？[y/n]："
      read -r -e reset_backup
      if [ "$reset_backup" != "y" ]; then
        backup_directories=("${old_dirs[@]}")
        skip_backup=true
      fi
    fi
  fi

  if [ "$skip_backup" != "true" ]; then
    color_text 33 1 "是否需要备份服务器上的目录？[y/n]："
    read -r -e need_backup
    while [[ ! $need_backup =~ ^[YyNn]$ ]]; do
      color_text 33 1 "输入无效，请输入[y/n]："
      read -r -e need_backup
    done
    if [[ ! $need_backup =~ ^[Yy]$ ]]; then
      color_text 32 1 "不备份服务器上的目录"
    else
      backup_directories=("/home/xct258/docker" "/home/xct258/apps")

      color_text 34 1 "默认已添加以下目录："
      for dir in "${backup_directories[@]}"; do
        color_text 32 1 "- $dir"
      done

      while true; do
        color_text 33 1 "请输入要备份的目录路径（如果不需要继续添加，请输入 'done'）："
        
        if [ ${#backup_directories[@]} -gt 0 ]; then
          color_text 34 1 "当前已添加的目录："
          for dir in "${backup_directories[@]}"; do
            color_text 32 1 "- $dir"
          done
          color_text 33 1 "可以继续添加，输入 'done' 完成添加"
        fi
        
        read -r -e directory
        
        if [[ $directory == "done" ]]; then
          break
        fi
        
        printf "%b" "$(color_text 33 1 "您输入的目录路径为：")"
        printf "%b" "$(color_text 32 1 "$directory")"
        printf "%b" "$(color_text 33 1 " 确认添加该目录吗？[y/n]：")"
        read -r -e confirm
        while [[ ! $confirm =~ ^[YyNn]$ ]]; do
          color_text 33 1 "输入无效，请输入[y/n]："
          read -r -e confirm
        done
        if [[ $confirm =~ ^[Yy]$ ]]; then
          backup_directories+=("$directory")
        else
          color_text 31 1 "取消添加目录：$directory"
        fi
      done
    fi
  fi

  # 将备份目录写入文件
  if [ ${#backup_directories[@]} -gt 0 ]; then
    if grep -q "^backup_directories=(" "$CONFIG_FILE" 2>/dev/null; then
      sed -i '/^backup_directories=(/,/^)/d' "$CONFIG_FILE"
    fi
    {
      echo "backup_directories=("
      for dir in "${backup_directories[@]}"; do
        echo "  \"$dir\""
      done
      echo ")"
    } >> "$CONFIG_FILE"
    color_text 32 1 "备份目录已保存到 $CONFIG_FILE"
  fi

  color_text 33 1 "请输入 7z 加密密码（直接回车使用默认密码）："
  read -r -e backup_password
  if [ -n "$backup_password" ]; then
    if grep -q "^BACKUP_PASSWORD=" "$CONFIG_FILE" 2>/dev/null; then
      sed -i "s/^BACKUP_PASSWORD=.*/BACKUP_PASSWORD=$backup_password/" "$CONFIG_FILE"
    else
      echo "BACKUP_PASSWORD=$backup_password" >> "$CONFIG_FILE"
    fi
  fi

  if ! grep -qxF "/root/apps/脚本/$server_name.sh" /etc/boot-exec/tasks.list 2>/dev/null; then
    echo "/root/apps/脚本/$server_name.sh" >> /etc/boot-exec/tasks.list
  fi

  # 添加ssh登陆提醒脚本
  if [ -f "/etc/profile.d/ssh_login_notify.sh" ]; then
    color_text 32 1 "删除ssh_login_notify.sh文件，重新创建"
    rm /etc/profile.d/ssh_login_notify.sh
  fi
  wget -O /etc/profile.d/ssh_login_notify.sh https://raw.githubusercontent.com/xct258/common/refs/heads/main/脚本工具/shell/服务器运维/ssh登录提醒.sh
  chmod +x /etc/profile.d/ssh_login_notify.sh
  color_text 32 1 "用户登陆ssh推送消息配置完成"

  # 启动脚本
  systemctl start boot-exec.service
fi

color_text 32 1 "执行成功"
