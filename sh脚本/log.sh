#!/bin/bash

# ==========================================
# 防止重复加载
# 如果变量 _LOG_LOADED 已定义且非空，说明脚本已加载过
# 直接 return 避免重复执行导致变量覆盖或性能浪费
# ==========================================
[[ -n "${_LOG_LOADED}" ]] && return
readonly _LOG_LOADED=1
# 标记脚本已加载，设为只读防止被意外修改

# ==========================================
# 全局关联数组
# _log_cleanup_done_map: 标记某应用目录是否已执行过日志清理
# _log_file_map:         保存每个应用当前正在写入的日志文件路径
# 使用 declare -gA 声明全局关联数组，在所有函数中可见
# ==========================================
declare -gA _log_cleanup_done_map
declare -gA _log_file_map

# ==========================================
# MQTT 推送配置变量说明
# 所有变量在 source 前/后均可设置，也可以在运行时通过
#   log_push_config <key> <value> 修改
# 变量清单：
#   LOG_MQTT_API_URL       MQTT 推送 API 地址（默认：http://127.0.0.1:8383/api/push）
#   LOG_MQTT_TOPIC_PREFIX  MQTT 主题前缀（默认：logs/）
#   LOG_MQTT_SERVER_NAME   服务器标识（默认：hostname）
#   LOG_MQTT_MODE          推送模式：append / overwrite（默认：append）
# 注意：这里不执行 := 赋值，所有默认值在函数调用时通过 :- 动态处理
#       这样确保 source 后修改变量依然生效
# ==========================================

# ==========================================
# _log_get_caller_base
# 私有函数。获取调用 log 函数的脚本名称（去掉 .sh 后缀）
# 原理：BASH_SOURCE[2] 指向调用 log 函数的脚本路径
# 例：若 web.sh source 本模块后调用 log，返回 "web"
# ==========================================
_log_get_caller_base() {
  local caller="${BASH_SOURCE[2]}"
  echo "$(basename "$caller" .sh)"
}

# ==========================================
# _log_cleanup_once
# 私有函数。清理指定应用目录下的旧日志文件
# 参数 $1: base - 应用名称
# 逻辑：
#   1. 检查该应用是否已清理过（防重复）
#   2. 拼接日志目录路径
#   3. 按文件修改时间排序，保留最新的 LOG_MAX_FILES 个
#   4. 删除空子目录
# ==========================================
_log_cleanup_once() {
  local base="$1"
  # base 参数标识哪个应用的日志目录需要清理

  if [[ -z "${_log_cleanup_done_map[$base]}" ]]; then
    # 如果该应用尚未执行过清理，则进入清理流程

    local dir="${LOG_BASE_DIR:-$(cd "$(dirname "${BASH_SOURCE[3]:-${BASH_SOURCE[2]}}")" && pwd)/logs}/$base"
    # 计算日志目录路径：优先使用 LOG_BASE_DIR 环境变量，否则以调用脚本所在目录的 logs/ 子目录为根

    if [[ -d "$dir" ]]; then
      # 目录存在才执行清理，避免初次使用时报错

      find "$dir" -type f -name "*.log" -printf '%T@ %p\n' | sort -nr | \
        tail -n +$(( ${LOG_MAX_FILES:-30} + 1 )) | awk '{print $2}' | xargs -r rm -f --
      # 1. find 查找所有 .log 文件，输出"修改时间戳 文件路径"
      # 2. sort -nr 按时间戳倒序（最新的在前）
      # 3. tail -n +N 跳过前 N-1 个（即保留最新的 N-1 个，删除其余）
      # 4. awk 取出文件路径列
      # 5. xargs rm -f 删除旧文件（-r 表示无输入时不执行）
      # LOG_MAX_FILES 默认 30，可通过环境变量自定义

      find "$dir" -type d -empty -delete
      # 删除清理后产生的空目录，保持目录结构整洁
    fi

    _log_cleanup_done_map[$base]=1
    # 标记该应用已清理，避免重复执行 find 和排序等耗时操作
  fi
}

# ==========================================
# _log_push
# 私有函数。推送消息到 MQTT（通过 HTTP API），返回 HTTP 状态码
# 参数：
#   $1 - topic   MQTT 主题（将拼接前缀）
#   $2 - message 消息内容
#   $3 - level   日志级别（默认 info），用于在消息中标记
# 流程：
#   1. 检查 curl 是否可用
#   2. 构造 JSON 请求体
#   3. curl POST 到 API，输出 HTTP 状态码
# 返回：HTTP 状态码（如 200），失败时返回空字符串
# ==========================================
_log_push() {
  local topic="$1" message="$2" level="${3:-info}"

  if ! command -v curl &>/dev/null; then
    echo "[log] curl not found" >&2
    return 1
  fi

  local server_name="${LOG_MQTT_SERVER_NAME:-$(hostname)}"
  # 服务器名称：优先环境变量，否则取本机 hostname

  local topic_prefix="${LOG_MQTT_TOPIC_PREFIX:-logs/}"
  local full_topic="${topic_prefix}${topic}"
  # 完整 MQTT 主题 = 前缀（默认 logs/）+ 传入的 topic

  local api_url="${LOG_MQTT_API_URL:-http://127.0.0.1:8383/api/push}"
  local mode="${LOG_MQTT_MODE:-append}"

  # 同步请求，仅输出 HTTP 状态码
  curl -s -o /dev/null -w "%{http_code}" -X POST "${api_url}" \
    -H "Content-Type: application/json" \
    -d "$(cat <<EOJSON
{
  "server_name": "${server_name}",
  "topic": "${full_topic}",
  "message": "**${level}** ${message}\n> $(date '+%Y-%m-%d %H:%M:%S')",
  "push_to_mqtt": true,
  "msg_type": "markdown",
  "mode": "${mode}"
}
EOJSON
    )"
}

# ==========================================
# log_push
# 公开函数。推送消息到 MQTT，并根据结果记录日志
# 参数：
#   $1 - topic   必填。MQTT 主题（不含前缀）
#   $2 - message 必填。消息内容
#   $3 - level   可选。日志级别（默认 info）
# 使用示例：
#   log_push "deploy/status" "部署完成" "success"
# ==========================================
log_push() {
  if [[ $# -lt 2 ]]; then
    # 参数不足时打印用法并返回错误
    echo "Usage: log_push <topic> <message> [level]" >&2
    return 1
  fi

  local http_code
  http_code=$(_log_push "$@")

  if [[ "$http_code" == "200" ]]; then
    log success "Push OK (200): $1"
  else
    log error "Push failed (${http_code:-curl error}): $1"
  fi
}

# ==========================================
# log_push_config
# 公开函数。运行时修改 MQTT 配置，无需重新 source
# 参数：
#   $1 - key   配置项名称（不区分大小写，支持：api_url / topic_prefix /
#              server_name / mode）
#   $2 - value 配置值
# 使用示例：
#   log_push_config api_url "http://10.0.0.1:8383/api/push"
#   log_push_config server_name "prod-web-01"
# ==========================================
log_push_config() {
  local key="$(echo "$1" | tr '[:upper:]' '[:lower:]')"
  local value="$2"

  if [[ $# -lt 2 ]]; then
    echo "用法: log_push_config <配置项> <值>" >&2
    echo "支持配置项: api_url, topic_prefix, server_name, mode" >&2
    return 1
  fi

  case "$key" in
    api_url)       LOG_MQTT_API_URL="$value" ;;
    topic_prefix)  LOG_MQTT_TOPIC_PREFIX="$value" ;;
    server_name)   LOG_MQTT_SERVER_NAME="$value" ;;
    mode)          LOG_MQTT_MODE="$value" ;;
    *)
      echo "未知配置项: $1" >&2
      echo "支持配置项: api_url, topic_prefix, server_name, mode" >&2
      return 1
      ;;
  esac
}

# ==========================================
# log_usage
# 打印详细的使用说明到标准错误流
# 当用户直接执行本脚本或传入无效参数时调用
# ==========================================
log_usage() {
  cat << EOF >&2
======================================================================
📖 Bash 日志模块 (Logger) 使用说明
======================================================================

【基本用法】
  source log.sh                   # 在业务脚本中引入
  log <日志级别> <日志消息>        # 写入日志

【强制打印到终端】
  log -f <级别> <消息>            # 即使重定向也打印到控制台

【日志级别】
  debug   -> 🐞 调试信息
  success -> 🎉 成功消息
  info    -> ✅ 常规信息
  warn    -> ⚠️ 警告信息
  error   -> ❌ 错误信息
  fatal   -> 💀 致命错误

【环境变量配置】（source 前/后均可设置，运行时修改立即生效）
  LOG_APP_NAME           应用名称，用作日志子目录及日志标签
                         （默认：调用脚本名，去掉 .sh）
  LOG_BASE_DIR           日志存储根目录
                         （默认：调用脚本所在目录下的 logs/）
  LOG_MAX_FILES          每个应用保留的最大日志文件数
                         （默认：30）
  LOG_MQTT_API_URL       MQTT 推送 API 地址
                         （默认：http://127.0.0.1:8383/api/push）
  LOG_MQTT_TOPIC_PREFIX  MQTT 主题前缀
                         （默认：logs/）
  LOG_MQTT_SERVER_NAME   推送时的服务器标识
                         （默认：本机 hostname）
  LOG_MQTT_MODE          推送模式：append（追加）/ overwrite（覆盖）
                         （默认：append）

【函数说明】
  log <级别> <消息>                      写入日志（文件 + 终端）
  log_push <主题> <消息> [级别]       推送消息到 MQTT，自动记录结果
  log_push_config <配置项> <值>           运行时修改 MQTT 配置
  log_reset_session                      重置当前会话的日志文件（跨天时使用）
  log_usage                              打印本说明

【无限循环脚本】
  常驻/无限循环脚本在每次循环开始时调用 log_reset_session() 即可生成新的日志文件
======================================================================
EOF
}

# ==========================================
# log（核心函数）
# 写入日志到文件、输出到终端
# 参数：
#   可选：-f                 强制打印到终端
#   必填：<level>            日志级别
#   必填：<message>          日志消息内容（支持空格，无需引号包裹）
# ==========================================
log() {
  local force_print=0
  # force_print: 是否强制打印到控制台（无视重定向）

  if [[ "$1" == "-f" ]]; then
    # 检查第一个参数是否为强制打印标志
    force_print=1
    shift
    # 移除该标志，后续参数依次前移
  fi

  case "$1" in
    debug|success|info|warn|error|fatal)
      level="$1"
      shift
      ;;
    *)
      # 非法的日志级别：报错并打印使用说明
      echo "无效的日志级别: $1" >&2
      log_usage
      return 1
      ;;
  esac

  local message="$*"
  # 剩余所有参数拼接为日志消息（支持包含空格的完整句子）

  local base="${LOG_APP_NAME:-$(_log_get_caller_base)}"
  # base 用于日志目录名和 MQTT 主题：优先 LOG_APP_NAME，否则自动识别调用脚本

  local tag="$(_log_get_caller_base)"
  # tag 用于日志行中的标签，始终以调用脚本名为准（即使设置了 LOG_APP_NAME）

  # ==========================================
  # 时间戳处理
  # ts:      完整时间戳，用于日志行内容（格式：2026-07-03 14:30:00）
  # ts_file: 目录层级，按年月日组织日志文件（格式：2026/07/03）
  # 分开两次 date 调用，避免每次日志都拼接字符串
  # ==========================================
  local ts ts_file
  ts="$(date '+%Y-%m-%d %H:%M:%S')"
  ts_file="$(date '+%Y/%m/%d')"

  # ==========================================
  # 符号与颜色分配
  # 每个日志级别对应一个 Emoji 符号和终端转义颜色码
  # fatal 使用红底白字（41;37）突出显示
  # ==========================================
  local symbol color_code
  local color_reset="\033[0m"
  # color_reset: 重置终端颜色，防止后续输出被染色

  case "$level" in
    debug)   symbol="🐞"; color_code="\033[38;5;244m" ;;  # 灰色
    success) symbol="🎉"; color_code="\033[32m" ;;         # 绿色
    info)    symbol="✅"; color_code="\033[36m" ;;         # 青色
    warn)    symbol="⚠️"; color_code="\033[33m" ;;         # 黄色
    error)   symbol="❌"; color_code="\033[31m" ;;         # 红色
    fatal)   symbol="💀"; color_code="\033[41;37m" ;;      # 红底白字
  esac

  # ==========================================
  # 日志文件路径管理
  # 如果当前应用尚未分配日志文件，则创建新的文件：
  #   1. 生成带日期的目录结构 {LOG_BASE_DIR}/{base}/{YYYY}/{MM}/{DD}
  #   2. 生成唯一文件名：{tag}_{时分秒}_{进程PID}.log
  # 不同应用（base）使用独立的日志文件，互不影响
  # ==========================================
  if [[ -z "${_log_file_map[$base]}" ]]; then
    # 当前应用还没有打开的文件，需要创建新的

    local log_dir="${LOG_BASE_DIR:-$(cd "$(dirname "${BASH_SOURCE[2]:-${BASH_SOURCE[1]}}")" && pwd)/logs}/$base/$ts_file"
    # 完整目录路径：根目录 + 应用名 + 日期层级

    mkdir -p "$log_dir"
    # 递归创建目录，已存在则静默跳过

    local unique_id="${tag}_$(date '+%H%M%S')_$$"
    # 唯一标识：调用脚本名 + 当前时间（时分秒）+ 进程 PID
    # $$ 确保同一进程内多次调用共享同一个文件

    _log_file_map[$base]="$log_dir/${unique_id}.log"
    # 记录到全局关联数组，后续日志直接追加到该文件
  fi

  local log_file="${_log_file_map[$base]}"
  # 取出当前应用的日志文件完整路径

  # ==========================================
  # 写入日志文件
  # plain_log 为纯文本格式，无转义字符
  # 格式：[时间] [标签] 符号 消息
  # ==========================================
  local plain_log="[$ts] [$tag] $symbol $message"
  echo "$plain_log" >> "$log_file"

  # ==========================================
  # 控制台输出
  # colored_log 带终端颜色转义码
  # 输出条件：是终端（-t 1）或 强制打印（force_print=1）
  # 即使脚本输出被重定向到文件，-f 参数也能确保日志在终端可见
  # ==========================================
  local colored_log="${color_code}[$ts] [$tag] $symbol $message${color_reset}"
  if [[ -t 1 || "$force_print" -eq 1 ]]; then
    echo -e "$colored_log"
    # -e 选项使转义序列 \033[...m 生效
  fi

  # ==========================================
  # 触发日志清理
  # 每次写入日志后检查是否需要清理旧文件
  # 内部有防重复机制，仅首次和执行log_reset_session函数后运行
  # ==========================================
  _log_cleanup_once "$base"
}

# ==========================================
# log_reset_session
# 公开函数。重置当前应用的日志文件缓存和清理标记
# 用途：无限循环/常驻后台的脚本，在每次循环开始时调用
# 效果：每次调用都会创建新的日志文件，并触发旧日志清理
# 注意：BASH_SOURCE[1] 是调用此函数的脚本路径
# ==========================================
log_reset_session() {
  local base="${LOG_APP_NAME:-$(basename "${BASH_SOURCE[1]}" .sh)}"
  # 获取当前应用名，与 log() 中保持一致

  unset "_log_file_map[$base]"
  # 清除文件路径缓存，下次 log() 会创建新文件

  unset "_log_cleanup_done_map[$base]"
  # 清除清理标记，下次 log() 会重新执行清理（删除旧日志）
}

# ==========================================
# 独立运行处理
# 判断当前脚本是被 source 引入还是直接执行
# BASH_SOURCE[0] == "${0}" 说明是直接执行（如 bash log.sh）
# 直接执行时提示用户并打印使用说明
# ==========================================
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  echo "💡 提示：这是一个日志模块库文件，请在其他脚本中使用 'source' 引入，而不是直接运行。" >&2
  echo "----------------------------------------------------------------------" >&2
  log_usage
  exit 0
fi
