#!/bin/bash

# 下面这些需要填写
# --------------------------------
API_KEY="" # Cloudflare 的 Global API Key
API_EMAIL=""  # Cloudflare 账户的邮箱
ZONE_ID="" # 域名的区域 ID
RECORD_NAME="" # 需要更新的 DNS 记录名称
# 读取服务器基本信息
source /root/apps/脚本/config/服务器基本信息.txt
# CloudflareST 的目录路径
cf_directory="/root/apps/CloudflareST" 
# 测速地址
speedtest="https://speedtest.xct258.top/200m" # 测速用的 URL
# 设置执行时间
schedule_sleep_time="7:00" # 每天的执行时间
# 设置休眠天数
schedule_sleep_time_d="2" # 休眠天数
# --------------------------------
# 上面的内容需要填写

# 获取当前时间
date_time=$(date +"%Y-%m-%d %H:%M") 

# 推送消息合并函数
message_ip() {
  message_ip="$server_name
$date_time

$RECORD_NAME

$message_v4

$message_v6" 
} 
# IPv4 消息格式化函数
message_ip_v4() {
  message_v4="ipv4地址优选
$max_ip_v4
延迟
$max_ping_v4
速度
$max_down_v4"
}
# IPv6 消息格式化函数
message_ip_v6() {
  message_v6="ipv6地址优选
$max_ip_v6
延迟
$max_ping_v6
速度
$max_down_v6"
}

# 计算休眠时间（以秒为单位）
sleep_days=$((schedule_sleep_time_d * 24 * 60 * 60))

# 切换到工作目录
cd $cf_directory
while true; do
    # 获取当前 DNS 记录的 IPv4 信息
    record_info_ipv4=$(curl -s -X GET "https://api.cloudflare.com/client/v4/zones/$ZONE_ID/dns_records?type=A&name=$RECORD_NAME" \
        -H "X-Auth-Email: $API_EMAIL" \
        -H "X-Auth-Key: $API_KEY" \
        -H "Content-Type: application/json"
    )

    # 提取 DNS 记录的 ID
    record_id_ipv4=$(echo "$record_info_ipv4" | grep -oP '(?<="id":")[^"]+' | head -1)
    # 提取 DNS 记录的 IP
    record_ip_ipv4=$(echo "$record_info_ipv4" | grep -oP '(?<="content":")[^"]+' | head -1)
    max_down_v4=0 # 初始化最大下载速度
    max_ping_v4=0 # 初始化最大延迟
    max_ip_v4=""   # 初始化最优IP

    success_ipv4=false  # 初始化成功标志
    for ((i=1; i<=3; i++)); do
        # 使用当前优选IP的ip地址进行测速
        $cf_directory/CloudflareST -ip $record_ip_ipv4 -url $speedtest
        bestip_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 1)
        bestping_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 5)
        bestdown_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 6)
        # 检查下载速度
        if (( $(echo "$bestdown_v4 < 10" | bc -l) )); then
            echo "尝试 $i: 下载速度小于10M，重新测速..."
        else
            max_down_v4=$bestdown_v4 # 初始化最大下载速度
            max_ping_v4=$bestping_v4 # 初始化最大延迟
            max_ip_v4=$bestip_v4   # 初始化最优IP
            success_ipv4=true
            break
        fi
    done
    # 如果三次测速都没有达到要求，则重新优选ip
    if [ "$success_ipv4" = false ]; then
        # 循环 3 次以测试不同的IP
        for ((i=1; i<=3; i++)); do
            # 执行 CloudflareST 工具，进行速度测试
            $cf_directory/CloudflareST -n 100 -f $cf_directory/ip.txt -url $speedtest 
            bestip_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 1)
            bestping_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 5)
            bestdown_v4=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 6)
            if (( $(echo "$bestdown_v4 < 10" | bc -l) )); then # 如果优选下载速度小于10M
                if (( $(echo "$bestdown_v4 > $max_down_v4" | bc -l) )); then # 如果优选下载速度大于当前最大下载速度
                    # 更新最大下载速度、延迟和 IP 地址
                    max_down_v4=$bestdown_v4
                    max_ping_v4=$bestping_v4
                    max_ip_v4=$bestip_v4
                fi
            else
                # 更新最大下载速度、延迟和 IP 地址
                max_down_v4=$bestdown_v4
                max_ping_v4=$bestping_v4
                max_ip_v4=$bestip_v4
                # 推出循环
                break
            fi
        done
        # 更新 Cloudflare 上的 DNS 记录
        update_result_v4=$(curl -s -X PUT "https://api.cloudflare.com/client/v4/zones/$ZONE_ID/dns_records/$record_id_ipv4" \
            -H "X-Auth-Email: $API_EMAIL" \
            -H "X-Auth-Key: $API_KEY" \
            -H "Content-Type: application/json" \
            --data '{"type":"A","name":"'"$RECORD_NAME"'","content":"'"$max_ip_v4"'","ttl":1,"proxied":false}'
        )
    fi
    message_ip_v4

    # IPv6 的处理逻辑与 IPv4 类似
    # 获取当前 DNS 记录的 IPv6 信息
    record_info_ipv6=$(curl -s -X GET "https://api.cloudflare.com/client/v4/zones/$ZONE_ID/dns_records?type=AAAA&name=$RECORD_NAME" \
        -H "X-Auth-Email: $API_EMAIL" \
        -H "X-Auth-Key: $API_KEY" \
        -H "Content-Type: application/json"
    )

    # 提取 DNS 记录的 ID
    record_id_ipv6=$(echo "$record_info_ipv6" | grep -oP '(?<="id":")[^"]+' | head -1)
    # 提取 DNS 记录的 IP
    record_ip_ipv6=$(echo "$record_info_ipv6" | grep -oP '(?<="content":")[^"]+' | head -1)
    max_down_v6=0 # 初始化最大下载速度
    max_ping_v6=0 # 初始化最大延迟
    max_ip_v6=""   # 初始化最优IP
    success_ipv6=false  # 初始化成功标志
    for ((i=1; i<=3; i++)); do
        # 使用当前优选IP的ip地址进行测速
        $cf_directory/CloudflareST -ip $record_ip_ipv6 -url $speedtest
        bestip_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 1)
        bestping_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 5)
        bestdown_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 6)
        # 检查下载速度
        if (( $(echo "$bestdown_v6 < 10" | bc -l) )); then
            echo "尝试 $i: 下载速度小于10M，重新测速..."
        else
            max_down_v6=$bestdown_v6 # 初始化最大下载速度
            max_ping_v6=$bestping_v6 # 初始化最大延迟
            max_ip_v6=$bestip_v6   # 初始化最优IP
            success_ipv6=true
            break
        fi
    done
    # 如果三次测速都没有达到要求，则重新优选ip
    if [ "$success_ipv6" = false ]; then
        # 循环 3 次以测试不同的IP
        for ((i=1; i<=3; i++)); do
            # 执行 CloudflareST 工具，进行速度测试
            $cf_directory/CloudflareST -n 100 -f $cf_directory/ipv6.txt -url $speedtest 
            bestip_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 1)
            bestping_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 5)
            bestdown_v6=$(sed -n '2p' $cf_directory/result.csv | cut -d ',' -f 6)
            if (( $(echo "$bestdown_v6 < 10" | bc -l) )); then # 如果优选下载速度小于10M
                if (( $(echo "$bestdown_v6 > $max_down_v6" | bc -l) )); then # 如果优选下载速度大于当前最大下载速度
                    # 更新最大下载速度、延迟和 IP 地址
                    max_down_v6=$bestdown_v6
                    max_ping_v6=$bestping_v6
                    max_ip_v6=$bestip_v6
                fi
            else
                # 更新最大下载速度、延迟和 IP 地址
                max_down_v6=$bestdown_v6
                max_ping_v6=$bestping_v6
                max_ip_v6=$bestip_v6
                # 推出循环
                break
            fi
        done
        # 更新 Cloudflare 上的 DNS 记录
        update_result_v6=$(curl -s -X PUT "https://api.cloudflare.com/client/v4/zones/$ZONE_ID/dns_records/$record_id_ipv6" \
            -H "X-Auth-Email: $API_EMAIL" \
            -H "X-Auth-Key: $API_KEY" \
            -H "Content-Type: application/json" \
            --data '{"type":"AAAA","name":"'"$RECORD_NAME"'","content":"'"$max_ip_v6"'","ttl":1,"proxied":false}'
        )
    fi
    message_ip_v6

    # 推送消息
    message_ip
    curl -s -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=优选ip&description=cf优选ip&channel=一般通知&content=${message_ip}" \
    >/dev/null
    # 删除临时文件
    rm $cf_directory/result.csv
    # 休眠到指定时间
    current_date=$(date +%Y-%m-%d)
    target_time="${current_date} $schedule_sleep_time"
    time_difference=$(( $(date -d "${target_time}" +%s) - $(date +%s) ))
    if [[ ${time_difference} -lt 0 ]]; then
        time_difference=$(( ${time_difference} + 86400 ))
    fi
    time_difference=$((time_difference + sleep_days))
    sleep ${time_difference}
done
