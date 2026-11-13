#!/bin/bash

# ==============================================================================
# 用途: 检查 GitHub 上的单个文件是否有更新，如果有则自动覆盖本地文件
# 返回值 (Exit Code):
#   0 - 成功跑完流程（不论是否有更新）
#   2 - 传入参数错误
#   3 - 网络故障或组件错误
# ==============================================================================

LOCAL_PATH="$1"
REPO="$2"
FILE_PATH="$3"
TOKEN="$4"
BRANCH="${5:-main}"

output_json() {
    local status="$1"
    local message="$2"
    local updated="$3"
    
    cat <<EOF
{
  "status": "$status",
  "message": "$message",
  "updated": $updated,
  "local_path": "$LOCAL_PATH",
  "repo": "$REPO",
  "file_path": "$FILE_PATH",
  "branch": "$BRANCH"
}
EOF
}

# 1. 校验基础参数
if [ -z "$LOCAL_PATH" ] || [ -z "$REPO" ] || [ -z "$FILE_PATH" ]; then
    output_json "error" "参数不足！使用方法: <本地路径> <仓库名> <文件在仓储路径> [TOKEN] [分支]" "false"
    exit 2
fi

# 2. 健壮性优化：提前检查并自动安装 curl
if ! command -v curl &> /dev/null; then
    if [ "$EUID" -ne 0 ] && ! command -v sudo &> /dev/null; then
        output_json "error" "系统缺少 curl 且当前用户无 root/sudo 权限，无法自动安装。" "false"
        exit 3
    fi

    # 识别常见的包管理器
    if command -v apt-get &> /dev/null; then
        [ "$EUID" -eq 0 ] && apt-get update -qq && apt-get install -y curl &> /dev/null || sudo apt-get update -qq && sudo apt-get install -y curl &> /dev/null
    elif command -v yum &> /dev/null; then
        [ "$EUID" -eq 0 ] && yum install -y curl &> /dev/null || sudo yum install -y curl &> /dev/null
    fi

    if ! command -v curl &> /dev/null; then
        output_json "error" "尝试自动安装 curl 失败，请手动安装后再试。" "false"
        exit 3
    fi
fi

# 3. MD5 处理
get_md5() {
    if command -v md5sum &> /dev/null; then
        md5sum "$1" | awk '{print $1}'
    elif command -v md5 &> /dev/null; then
        md5 -q "$1"
    else
        # 如果连 md5 都没有，降级用 cksum 或 sha256sum
        openssl dgst -md5 "$1" | awk '{print $2}'
    fi
}

# 4. 构建请求
if [ -n "$TOKEN" ]; then
    URL="https://api.github.com/repos/${REPO}/contents/${FILE_PATH}?ref=${BRANCH}"
    AUTH_HEADER="Authorization: token ${TOKEN}"
    ACCEPT_HEADER="Accept: application/vnd.github.v3.raw"
else
    URL="https://raw.githubusercontent.com/${REPO}/${BRANCH}/${FILE_PATH}"
    AUTH_HEADER="X-Dummy: None"
    ACCEPT_HEADER="Accept: */*"
fi

TMP_FILE="/tmp/gh_update_$(basename "$LOCAL_PATH").tmp"

# 5. 下载远程文件 (增加连接超时 10 秒，传输总超时 60 秒)
CURL_OPTS="-s -w %{http_code} --connect-timeout 10 -m 60"
if [ -n "$TOKEN" ]; then
    HTTP_STATUS=$(curl $CURL_OPTS -o "$TMP_FILE" -H "$AUTH_HEADER" -H "$ACCEPT_HEADER" "$URL")
else
    HTTP_STATUS=$(curl $CURL_OPTS -o "$TMP_FILE" "$URL")
fi

CURL_EXIT_CODE=$?

if [ $CURL_EXIT_CODE -ne 0 ]; then
    output_json "error" "curl 下载失败，网络错误代码: $CURL_EXIT_CODE" "false"
    rm -f "$TMP_FILE"
    exit 3
fi

# 6. 检查状态码 (改为字符串比对，防止 HTTP_STATUS 为空时引发语法错误)
if [ "$HTTP_STATUS" != "200" ]; then
    output_json "error" "GitHub 请求失败，HTTP 状态码: $HTTP_STATUS" "false"
    rm -f "$TMP_FILE"
    exit 3
fi

# 7. 如果本地文件不存在，直接初始化
if [ ! -f "$LOCAL_PATH" ]; then
    mkdir -p "$(dirname "$LOCAL_PATH")"
    mv "$TMP_FILE" "$LOCAL_PATH"
    if [ -x "$LOCAL_PATH" ] || [[ "$LOCAL_PATH" == *.sh ]]; then
        chmod +x "$LOCAL_PATH"
    fi
    output_json "success" "本地文件不存在，已成功创建初始版本文件。" "true"
    exit 0
fi

# 8. 对比哈希值决定是否更新
LOCAL_MD5=$(get_md5 "$LOCAL_PATH")
REMOTE_MD5=$(get_md5 "$TMP_FILE")

if [ "$LOCAL_MD5" != "$REMOTE_MD5" ]; then
    mv "$TMP_FILE" "$LOCAL_PATH"
    if [ -x "$LOCAL_PATH" ] || [[ "$LOCAL_PATH" == *.sh ]]; then
        chmod +x "$LOCAL_PATH"
    fi
    output_json "success" "检测到远程文件更改，本地文件已自动更新完成。" "true"
    exit 0
else
    rm -f "$TMP_FILE"
    output_json "success" "文件无变化，本地已经是最新版本。" "false"
    exit 0
fi
