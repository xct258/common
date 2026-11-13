#!/bin/bash
set -euo pipefail
set -x

# === 基础配置 ===
API_KEY=""
API_EMAIL=""
ZONE_ID_1=""
ZONE_ID_2=""
RECORD_NAME=""
SERVER_NAME_2=""
SERVER_NAME="" # <-- 设定一个默认值，防止配置文件缺失时触发 set -u 崩溃
CONFIG_FILE="/root/apps/脚本/config/服务器基本信息.txt"

# 加载额外配置信息（如有）
[[ -f "$CONFIG_FILE" ]] && source "$CONFIG_FILE"

# 设备列表（记录名 + IPv6 后缀）
declare -A devices=(
  [pve-2-debian12-1]="pve-2-debian12-1.xct258.cn.eu.org be24:11ff:fedc:e13"
  #[pve-1-win11-1]="win11-1.p1.xct258.top 38ae:c085:561a:d2e7"
)

# === 工具函数 ===

# 错误推送
push_error() {
  local title="$1"
  local detail="$2"
  curl -s --max-time 10 --fail -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=$title&channel=错误通知&description=失败&content=$detail" || true
}

# 成功推送
push_success() {
  local message="$1"
  curl -s --max-time 10 --fail -X POST "https://msgpusher.xct258.top/push/root" \
    -d "title=更新ipv6解析&channel=一般通知&description=成功&content=$message" || true
}

# 成功推送消息构建函数
build_message() {
  local old_prefix="$1"
  local new_prefix="$2"
  local datetime="$3"
  echo "$server_name
$datetime

${server_name_2}的IPv6地址已更新解析
旧IPv6前缀：
$old_prefix
新IPv6前缀：
$new_prefix"
}

# 获取域名对应的 Zone ID
get_zone_id() {
  case "$1" in
    *.xct258.cn.eu.org) echo "$ZONE_ID_1" ;;
    *.xct258.top) echo "$ZONE_ID_2" ;;
    *) push_error "Zone ID 获取失败" "未知域名后缀：$1"; exit 1 ;;
  esac
}

# 获取 DNS 记录的当前 IPv6 地址
get_cf_record_ipv6_prefix() {
  local name="$1"
  local zone_id="$2"
  local response
  response=$(curl -s --fail -X GET \
    -H "X-Auth-Email: $API_EMAIL" -H "X-Auth-Key: $API_KEY" -H "Content-Type: application/json" \
    "https://api.cloudflare.com/client/v4/zones/$zone_id/dns_records?type=AAAA&name=$name") || return 1

  echo "$response" | jq -r '.result[0].content' | cut -d':' -f1-4
}

# 获取 DNS 记录 ID
get_cf_record_id() {
  local name="$1"
  local zone_id="$2"
  curl -s --fail -X GET \
    -H "X-Auth-Email: $API_EMAIL" -H "X-Auth-Key: $API_KEY" -H "Content-Type: application/json" \
    "https://api.cloudflare.com/client/v4/zones/$zone_id/dns_records?type=AAAA&name=$name" \
    | jq -r '.result[0].id'
}

# 更新 Cloudflare DNS 记录
update_cf_record() {
  local zone_id="$1"
  local record_id="$2"
  local name="$3"
  local ipv6="$4"

  local response
  response=$(curl -s --fail -X PUT \
    -H "X-Auth-Email: $API_EMAIL" -H "X-Auth-Key: $API_KEY" -H "Content-Type: application/json" \
    --data "{\"type\":\"AAAA\",\"name\":\"$name\",\"content\":\"$ipv6\",\"ttl\":1,\"proxied\":false}" \
    "https://api.cloudflare.com/client/v4/zones/$zone_id/dns_records/$record_id")

  echo "$response" | jq -e '.success' > /dev/null || {
    push_error "更新失败" "设备：$name 响应：$response"
    return 1
  }
}

# 获取当前服务器 IPv6 前缀
get_local_ipv6_prefix() {
  local urls=(
    "https://6.ipw.cn"
    "https://api64.ipify.org"
    "https://ifconfig.co/ip"
    "https://ipv6.icanhazip.com"
  )

  local ipv6
  for url in "${urls[@]}"; do
    ipv6=$(curl -6 -s --max-time 5 "$url" || true)
    if [[ -n "$ipv6" ]]; then
      echo "$ipv6" | cut -d':' -f1-4
      return 0
    fi
  done

  return 1
}

# === 初始化缓存（带断网容错重试机制） ===
declare -A device_record_ids

echo "正在初始化 Cloudflare Record ID 缓存..."
for device in "${!devices[@]}"; do
  device_info="${devices[$device]}"
  record_name=$(echo "$device_info" | awk '{print $1}')
  zone_id=$(get_zone_id "$record_name")
  
  # 使用无限循环进行初始化重试，直到网络正常并获取成功
  while true; do
    record_id=$(get_cf_record_id "$record_name" "$zone_id") || record_id="null"
    
    if [[ -n "$record_id" && "$record_id" != "null" ]]; then
      device_record_ids[$device]="$record_id"
      echo "成功缓存 $record_name 的 ID: $record_id"
      break # 成功获取，跳出当前设备的重试循环
    else
      echo "获取 $record_name 的 ID 失败，可能网络未就绪，10秒后重试..."
      sleep 10
    fi
  done
done
echo "缓存初始化完成，脚本开始正式守护运行。"


# === 主逻辑循环 ===
while true; do
  current_time=$(date +"%Y-%m-%d %H:%M")

  cf_ipv6_prefix=$(get_cf_record_ipv6_prefix "$RECORD_NAME" "$ZONE_ID_1") || {
    sleep 240; continue
  }

  local_ipv6_prefix=$(get_local_ipv6_prefix) || {
    push_error "本地 IPv6 获取失败" "无法获取当前服务器 IPv6"
    sleep 240; continue
  }

  # 判断是否需要更新
  if [[ "$cf_ipv6_prefix" != "$local_ipv6_prefix" ]]; then
    for device in "${!devices[@]}"; do
      device_info="${devices[$device]}"
      record_name=$(echo "$device_info" | awk '{print $1}')
      suffix=$(echo "$device_info" | awk '{print $2}')
      new_ipv6="${local_ipv6_prefix}:${suffix}"

      zone_id=$(get_zone_id "$record_name")
      record_id="${device_record_ids[$device]}"

      update_cf_record "$zone_id" "$record_id" "$record_name" "$new_ipv6" || continue
    done

    # 推送成功信息
    success_msg=$(build_message "$cf_ipv6_prefix" "$local_ipv6_prefix" "$current_time")
    push_success "$success_msg"
  fi

  sleep 3600  # 每小时检查一次
done
