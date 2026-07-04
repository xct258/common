#!/bin/bash

source /root/apps/脚本/config/服务器基本信息.txt

# 服务器ip地址
ipv6_address=$(curl -6 -s --connect-timeout 5 icanhazip.com || true)
ipv4_address=$(curl -4 -s --connect-timeout 5 icanhazip.com || true)

# 获取当前用户名
username1=$(whoami)

# 获取登陆用户名
username2=$(who am i | awk '{print $1}')

# 获取登录ip地址
ip_address=$(who am i | awk '{gsub(/[()]/, "", $NF); print $NF}')

# 时间
date_time=$(date +"%Y-%m-%d %H:%M")

# 消息合并函数
message_hs_1(){
message="$server_name
$date_time

⚠登陆提醒⚠

用户
$username1

$(if [ -n "$ip_address" ]; then
   echo "登录ip
$ip_address"
else
   echo "无法获取登陆ip"
fi)

服务器ip
$ipv4_address
$ipv6_address"
}

message_hs_2(){
message="$server_name
$date_time

⚠登陆提醒⚠

用户
${username2}通过sudo成功获取${username1}权限

$(if [ -n "$ip_address" ]; then
   echo "登录ip
$ip_address"
else
   echo "无法获取登陆ip"
fi)

服务器ip
$ipv4_address
$ipv6_address"
}

if [ "$username1" = "$username2" ] || [ -z "$username2" ]; then
   message_hs_1
elif [ "$username1" != "$username2" ] && [ -n "$username2" ]; then
   message_hs_2
fi

# 推送消息
curl -s -X POST "https://msgpusher.xct258.top/push/root"  -d "title=服务器ssh登陆提醒&description=$server_name&channel=vps登陆提醒&content=$message"  > /dev/null 2>&1 & disown
