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

apt update

# 配置cls清屏
# 检查 /etc/bash.bashrc 文件中是否已经存在 alias cls
if ! grep -q "alias cls='clear'" /etc/bash.bashrc; then
    # 如果没有，则添加别名
    echo "alias cls='clear'" >> /etc/bash.bashrc
    color_text 32 1 "成功设置cls命令实现清屏"
fi
# 使配置文件生效
source /etc/bash.bashrc

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
  color_text 31 1 "中文语言环境设置成功，请重新连接SSH"
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

# 定义github的令牌
while true; do
    # 提示用户输入 GitHub 令牌
    color_text 33 1 "请输入 GitHub 的令牌"
    read -r github_token

    # 确认用户输入的令牌
    color_text 33 1 "您输入的 GitHub 令牌是: "
    color_text 32 1 "$github_token"
    color_text 33 1 "请确认输入的令牌是否正确[y/n]: "
    read -r confirmation

    if [ "$confirmation" = "y" ]; then
        color_text 32 1 "令牌已确认。"
        break  # 退出循环
    else
        color_text 31 1 "令牌未确认，请重新输入。"
    fi
done

# 检查是否已经配置了 DHCP
if grep -q 'iface .* inet dhcp' /etc/network/interfaces; then
    color_text 32 1 "DHCP 已经配置，跳过此步骤。"
else
  # 询问用户是否使用 DHCP 获取 IP 地址
  while true; do
      color_text 33 1 "是否使用 DHCP 获取 IP 地址？[y/n]: "
      color_text 31 1 "只能在本地服务器中启用DHCP"
      read -r use_dhcp
  
      if [[ "$use_dhcp" == "y" ]]; then
          color_text 32 1 "配置 DHCP 获取 IP 地址。"
          cp /etc/network/interfaces /etc/network/interfaces.bak
          sed -i '/iface .* inet static/!b; n; :a; N; $!ba; s/.//; P; D' /etc/network/interfaces
          sed -i '/iface .* inet static/{s/static/dhcp/; n; :a; N; $!ba; d}' /etc/network/interfaces
          break  # 退出循环
      elif [[ "$use_dhcp" == "n" ]]; then
          color_text 32 1 "不配置 DHCP"
          break  # 退出循环
      else
          color_text 31 1 "无效输入，请输入 y 或 n。"
      fi
  done
fi

# 安装7zip
if [ -e "/bin/7zz" ]; then
  color_text 32 1 "7z已安装"
else
  # 获取最新版本号（例如 24.09）
  latest_ver_7z=$(curl -s https://www.7-zip.org/download.html | grep -oP '7z\K[0-9]{4}' | head -1)
  # 使用 uname 命令获取服务器的架构信息，并使用 grep 过滤出包含 "x86_64" 或 "aarch64" 的行
  arch=$(uname -m)
  # 检查架构信息，根据结果下载不同架构的7zip（版本为2409，请注意更新最新的7zip下载链接）
  if [[ $arch == *"x86_64"* ]]; then
    wget -O 7zz.tar.xz https://www.7-zip.org/a/7z${latest_ver_7z}-linux-x64.tar.xz
  elif [[ $arch == *"aarch64"* ]]; then
    wget -O 7zz.tar.xz https://www.7-zip.org/a/7z${latest_ver_7z}-linux-arm64.tar.xz
  else
    color_text 31 1 "无法确定服务器架构，请手动安装7z"
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
    read -r install_docker
    if [ "$install_docker" == "y" ]; then
      # docker安装官方一键脚本
      curl -fsSL https://get.docker.com | bash -s docker
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

# 配置开机启动rc.local
# 检测rc.local文件是否存在
if [ -f "/etc/rc.local" ]; then
  color_text 32 1 "rc.local文件存在，无需再次执行"
else
  # 下载动rc.local文件
  wget --inet4-only --header="Authorization: token $github_token" -O /etc/rc.local https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/服务器相关/other/rc.local
  chmod +x /etc/rc.local
  systemctl enable --now rc-local
  color_text 32 1 "rc-local开机启动配置成功"
fi

# 检测服务器的ipv4地址和ipv6地址
ipv6_address=$(curl -6 -s --connect-timeout 10 icanhazip.com || true)
ipv4_address=$(curl -4 -s --connect-timeout 10 icanhazip.com || true)
# 检测ipv6地址是否存在
if [ -n "$ipv6_address" ]; then
  color_text 32 1 "IPv6地址存在，跳过配置IPv6"
else
  # 添加IPv6
  # 获取第一个非回环接口的名称
  interface=$(ip -o -4 route show to default | awk '{print $5; exit}')
  if [ -z "$interface" ]; then
    color_text 33 1 "未找到可用的网络接口"
  else
    # 直接进入 IPv6 添加交互（不再检测 iface .* inet6 dhcp）
    valid_choice_ipv6=false
    while [ "$valid_choice_ipv6" = false ]; do
      color_text 33 1 "是否添加IPv6配置？[y/n]: "
      read -r ipv6_automatic
      case "$ipv6_automatic" in
        y|Y)
          # 仅追加 up/down 脚本到 /etc/network/interfaces（不写 iface 行），并避免重复添加
          if ! grep -Fq "up sleep 10 && /sbin/dhclient -6 $interface" /etc/network/interfaces; then
            echo "up sleep 10 && /sbin/dhclient -6 $interface" >> /etc/network/interfaces
            echo "down /sbin/dhclient -6 -r $interface" >> /etc/network/interfaces
            color_text 32 1 "up/down DHCPv6 命令已追加到 /etc/network/interfaces；接口启动时将延时触发 DHCPv6 请求"
            color_text 32 1 "正在重启网络服务以应用更改，请稍候..."
            systemctl restart networking
          else
            color_text 32 1 "up/down DHCPv6 命令已存在于 /etc/network/interfaces，跳过添加"
          fi
          valid_choice_ipv6=true
          ;;
        n|N)
          color_text 32 1 "已取消添加IPv6"
          valid_choice_ipv6=true
          ;;
        *)
          color_text 31 1 "无效的输入，请重新输入"
          ;;
      esac
    done
  fi
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
    read -r install_rclone
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
            read -r overwrite_config
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
            color_text 33 1 "注意：有效期到2027年7月"
            read -r rclone_config
            case "$rclone_config" in
              y)
                mkdir -p /root/.config/rclone
                wget --header="Authorization: token $github_token" -O "$CONFIG_FILE_rclone" https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/rclone/rclone.conf
                color_text 32 1 "rclone配置文件已加载"
                break
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

# 检测docker是否在服务器启动时启动
if systemctl is-enabled docker.service &> /dev/null; then
  # 取消docker开机自动启动
  systemctl disable docker.socket
  systemctl disable docker.service
  color_text 32 1 "docker开机自动启动取消成功"
fi
# 将docker启动命令添加到rc.local
if grep -q "systemctl start docker.service" /etc/rc.local; then
  color_text 32 1 "docker启动命令已添加到/etc/rc.local，无需添加"
else
  sed -i '$i\systemctl start docker.service' /etc/rc.local
  color_text 32 1 "docker启动命令已添加到/etc/rc.local"
fi

# swap交换分区
# 函数：删除分区（指定设备和分区号）
delete_partition() {
  local device=$1
  local part_num=$2
  fdisk /dev/"$device" <<EOF
d
$part_num
w
EOF
}
# 函数：重建主分区（GPT类型）
recreate_partition_gpt() {
  local device=$1
  local part_num=$2
  fdisk /dev/"$device" <<EOF
d
$part_num
n
$part_num


w
EOF
}
# 主程序开始
if [ -n "$(swapon -s)" ]; then
  while true; do
    color_text 33 1 "是否需要删除 swap 分区？[y/n]: "
    read -r keep_swap
    if [ "$keep_swap" == "y" ]; then
      # 获取swap信息
      swap_device=$(swapon -s | awk 'NR==2{print $1}')
      swap_uuid=$(blkid -s UUID -o value "$swap_device")
      swap_number=$(echo "$swap_device" | grep -oE '[0-9]+$')
      root_partition=$(df / | awk 'NR==2 {print $1}')
      root_device=$(lsblk -no pkname "$root_partition")
      root_partition_number=$(echo "$root_partition" | grep -oE '[0-9]+$')

      # 禁用 swap 并更新 fstab
      swapoff "$swap_device"
      sed -i "/UUID=$swap_uuid/d" /etc/fstab
      color_text 32 1 "Swap 分区已禁用，并从 /etc/fstab 移除"

      # 删除 swap 分区
      delete_partition "$root_device" "$swap_number"
      color_text 32 1 "Swap 分区已删除"

      # 判断分区表类型
      disk_label_type=$(fdisk -l /dev/"$root_device" | grep -E 'Disklabel type|磁盘标签类型' | sed 's/.*[：:] *//')

      color_text 33 1 "开始调整分区表以扩展根分区..."

      # 依据分区表类型重建根分区
      if [[ "$disk_label_type" == "dos" ]]; then
        color_text 33 1 "MBR 分区表，跳过分区调整"
      elif [[ "$disk_label_type" == "gpt" ]]; then
        recreate_partition_gpt "$root_device" "$root_partition_number"
        # 扩展文件系统
        fs_type=$(lsblk -no FSTYPE "$root_partition")
        case "$fs_type" in
          ext4)
            color_text 32 1 "扩展 ext4 文件系统..."
            resize2fs "$root_partition"
            color_text 32 1 "ext4 文件系统扩展完成"
            ;;
          xfs)
            color_text 32 1 "扩展 xfs 文件系统..."
            xfs_growfs "$root_partition"
            color_text 32 1 "xfs 文件系统扩展完成"
            ;;
          *)
            color_text 31 1 "不支持的文件系统类型: $fs_type"
            ;;
        esac
      else
        color_text 31 1 "不支持的分区表类型: $disk_label_type"
        break
      fi
      break
    elif [ "$keep_swap" == "n" ]; then
      color_text 32 1 "不删除 swap 分区"
      break
    else
      color_text 31 1 "无效输入，请输入 'y' 或 'n'"
    fi
  done
else
  color_text 32 1 "当前没有启用的 swap 分区"
fi

# 添加服务器备份/监控脚本
while true; do
  color_text 32 1 "输入服务器名称"
  color_text 33 1 "示例：甲骨文-1-debian-1"
  read -r server_name

  # 显示用户输入的服务器名称并确认是否重新输入
  color_text 32 1 "您输入的服务器名称是："
  color_text 32 1 "$server_name"
  color_text 33 1 "是否确认服务器名称？[y/n]: "
  read -r input_name_sever
    if [ "$input_name_sever" == "y" ]; then
      # 创建必要的文件夹
    if [ -d "/root/apps/脚本/config" ]; then
      color_text 32 1 "/root/apps/脚本/config文件夹已存在，跳过创建"
    else
      mkdir -p "/root/apps/脚本/config"
    fi
    # 将服务器名称写入文件
    echo server_name="$server_name" > /root/apps/脚本/config/服务器基本信息.txt
    # 退出循环
    break
  fi
done
color_text 33 1 "是否需要自动加载备份/监控脚本？[y/n]: "
read -r sever_backup

# 验证用户输入
while [[ ! $sever_backup =~ ^[YyNn]$ ]]; do
  color_text 33 1 "输入无效，请输入[y/n]："
  read -r sever_backup
done
if [[ ! $sever_backup =~ ^[Yy]$ ]]; then
  color_text 32 1 "不自动配置脚本"
  # 询问用户是否安装用户登陆ssh推送消息脚本
  color_text 33 1 "是否添加用户登陆ssh推送消息脚本？[y/n]: "
  read -r install_ssh_push

  # 验证用户输入
  while [[ ! $install_ssh_push =~ ^[YyNn]$ ]]; do
    color_text 33 1 "输入无效，请输入[y/n]："
    read -r install_ssh_push
  done
  if [[ ! $install_ssh_push =~ ^[Yy]$ ]]; then
    color_text 32 1 "不添加ssh推送消息脚本"
  else
    wget --header="Authorization: token $github_token" -O /etc/profile.d/ssh_login_notify.sh https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/服务器相关/other/ssh登录提醒.sh
    chmod +x /etc/profile.d/ssh_login_notify.sh
    color_text 32 1 "用户登陆ssh推送消息配置完成"
  fi
else
  # 下载脚本
  wget --header="Authorization: token $github_token" -O "$server_name".sh https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/服务器相关/sh/服务器备份监控脚本.sh
  chmod +x "$server_name".sh
  mv "$server_name".sh "/root/apps/脚本/$server_name.sh"

  # 询问用户是否需要输入可用时间
  color_text 33 1 "是否需要输入${server_name}的可用时间？[y/n]："
  read -r need_input
  # 验证用户输入
  while [[ ! $need_input =~ ^[YyNn]$ ]]; do
    color_text 33 1 "输入无效，请输入[y/n]："
    read -r need_input
  done
  if [[ ! $need_input =~ ^[Yy]$ ]]; then
    color_text 32 1 "您选择不输入可用时间，将跳过该步骤。"
  else
    while true; do
      color_text 33 1 "请输入${server_name}可用时间（格式：YYYY-MM-DD）："
      read -r server_free_date
      # 检查格式是否正确（4位年-2位月-2位日）
      if [[ ! $server_free_date =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
        color_text 31 1 "格式错误，请重新输入（例如 2025-10-07）。"
        continue
      fi
      # 使用 date 验证并检查是否被自动修正
      valid_date=$(date -d "$server_free_date" "+%Y-%m-%d" 2>/dev/null)

      if [[ "$valid_date" == "$server_free_date" ]]; then
        color_text 32 1 "您输入的日期为：$server_free_date"
        break
      else
        color_text 31 1 "日期无效，请重新输入。"
      fi
    done
  fi

  # 将服务器可用时间写入文件
  echo server_free_datetime="$server_free_date" >> /root/apps/脚本/config/服务器基本信息.txt

  # 询问用户是否需要备份服务器上的目录
  color_text 33 1 "是否需要备份服务器上的目录？[y/n]："
  read -r need_backup
  # 验证用户输入
  while [[ ! $need_backup =~ ^[YyNn]$ ]]; do
    color_text 33 1 "输入无效，请输入[y/n]："
    read -r need_backup
  done
  if [[ ! $need_backup =~ ^[Yy]$ ]]; then
    color_text 32 1 "不备份服务器上的目录"
  else
    # 初始化备份目录数组，默认包含两个目录
    backup_directories=("/home/xct258/docker" "/home/xct258/apps")

    # 提示用户默认添加的目录
    color_text 34 1 "默认已添加以下目录："
    for dir in "${backup_directories[@]}"; do
      color_text 32 1 "- $dir"
    done

    while true; do
      color_text 33 1 "请输入要备份的目录路径（如果不需要继续添加，请输入 'done'）："
      
      # 显示已添加的目录
      if [ ${#backup_directories[@]} -gt 0 ]; then
        color_text 34 1 "当前已添加的目录："
        for dir in "${backup_directories[@]}"; do
          color_text 32 1 "- $dir"
        done
        color_text 33 1 "可以继续添加，输入 'done' 完成添加"
      fi
      
      read -r directory
      
      # 检查用户是否完成输入
      if [[ $directory == "done" ]]; then
        break
      fi
      
      # 确认添加目录
      printf "%b" "$(color_text 33 1 "您输入的目录路径为：")"
      printf "%b" "$(color_text 32 1 "$directory")"
      printf "%b" "$(color_text 33 1 " 确认添加该目录吗？[y/n]：")"
      read -r confirm
      while [[ ! $confirm =~ ^[YyNn]$ ]]; do
        color_text 33 1 "输入无效，请输入[y/n]："
        read -r confirm
      done
      if [[ $confirm =~ ^[Yy]$ ]]; then
        backup_directories+=("$directory")
      else
        color_text 31 1 "取消添加目录：$directory"
      fi
    done

    # 将备份目录写入文件
    {
      echo "backup_directories=("
      for dir in "${backup_directories[@]}"; do
        echo "  \"$dir\""
      done
      echo ")"
    } >> "/root/apps/脚本/config/服务器基本信息.txt"
    color_text 32 1 "备份目录已保存到 /root/apps/脚本/config/服务器基本信息.txt"

  fi

  # 检查脚本是否已添加到开机启动
  if grep -q "/root/apps/脚本/$server_name.sh>/dev/null 2>&1 &" /etc/rc.local; then
    color_text 32 1 "'$server_name'脚本开机启动已添加到/etc/rc.local，无需添加"
  else
    # 将脚本添加到开机启动
    sed -i '$i\'/root/apps/脚本/$server_name.sh'>/dev/null 2>&1 &' /etc/rc.local
    color_text 32 1 "'$server_name'脚本开机启动已添加到/etc/rc.local"
  fi
  # 添加ssh登陆提醒脚本
  if [ -f "/etc/profile.d/ssh_login_notify.sh" ]; then
    color_text 32 1 "删除ssh_login_notify.sh文件，重新创建"
    rm /etc/profile.d/ssh_login_notify.sh
  fi
  wget --header="Authorization: token $github_token" -O /etc/profile.d/ssh_login_notify.sh https://raw.githubusercontent.com/xct258/Documentation/refs/heads/main/服务器相关/other/ssh登录提醒.sh
  chmod +x /etc/profile.d/ssh_login_notify.sh
  color_text 32 1 "用户登陆ssh推送消息配置完成"

  # 启动脚本
  nohup "/root/apps/脚本/$server_name.sh">/dev/null 2>&1 &
fi

color_text 32 1 "执行成功"
color_text 32 1 "服务器ipv4地址:$ipv4_address"
color_text 32 1 "服务器ipv6地址:$ipv6_address"

if [ "$keep_swap" == "y" ]; then
  color_text 31 1 "swap分区已经删除，但是需要重新生效！"
fi

rm -rf ./debian
