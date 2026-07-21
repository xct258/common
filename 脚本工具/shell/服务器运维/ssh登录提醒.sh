#!/bin/bash

source /root/apps/脚本/config/服务器基本信息.txt

# 服务器ip地址
ipv4_address=$(curl -4 -s --connect-timeout 5 api.ipify.org || true)
ipv6_address=$(curl -6 -s --connect-timeout 5 api6.ipify.org || true)

# 获取当前用户名
username1=$(whoami)

# 获取登陆用户名和登录ip
login_info=$(who am i 2>/dev/null)
if [ -n "$login_info" ]; then
  username2=$(echo "$login_info" | awk '{print $1}')
  ip_address=$(echo "$login_info" | awk '{gsub(/[()]/, "", $NF); print $NF}')
else
  username2=""
  ip_address=""
fi

# 时间
date_time=$(date +"%Y-%m-%d %H:%M")

# 消息合并函数
message_hs_1(){
message="## $server_name
$date_time

**⚠ 登陆提醒 ⚠**

- **用户**: $username1
- **登录IP**: ${ip_address:-无法获取}
- **服务器IPv4**: $ipv4_address
- **服务器IPv6**: $ipv6_address
"
}

message_hs_2(){
message="## $server_name
$date_time

**⚠ 登陆提醒 ⚠**

- **用户**: ${username2} → ${username1}（sudo提权）
- **登录IP**: ${ip_address:-无法获取}
- **服务器IPv4**: $ipv4_address
- **服务器IPv6**: $ipv6_address
"
}

if [ "$username1" = "$username2" ] || [ -z "$username2" ]; then
   message_hs_1
elif [ "$username1" != "$username2" ] && [ -n "$username2" ]; then
   message_hs_2
fi

# 推送消息
curl -s -X POST "https://push-server.o1-1.xct258.top/api/push" \
  -H "Content-Type: application/json" \
  -d "$(jq -n --arg sn "$server_name" --arg topic "server/$server_name" --arg msg "$message" '{
    server_name: $sn,
    topic: $topic,
    message: $msg,
    push_to_mqtt: true,
    msg_type: "markdown",
    mode: "append"
  }')" > /dev/null 2>&1 & disown
