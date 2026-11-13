#!/bin/bash

# ==============================================================================
# 用途: 检查 GitHub 上的单个文件是否有更新 (并发安全/严格防漏 版)
# ==============================================================================

LOCAL_PATH="$1"
REPO="$2"
FILE_PATH="$3"
TOKEN="$4"
BRANCH="${5:-main}"

# 🌟 优化 1: 为临时文件加入当前进程 PID ($$) 避免并发写入冲突
TMP_FILE="/tmp/gh_update_$(basename "$LOCAL_PATH")_$$.tmp"

# 🌟 优化 2: 注册退出钩子，无论以何种姿势结束退出，确保垃圾文件被清理
trap 'rm -f "$TMP_FILE"' EXIT

output_json() {
    local status="$1"
    # 替换 message 中可能包含的双引号为转义双引号，防止 JSON 解析崩溃
    local message="${2//\\/\\\\}"
    message="${message//\"/\\\"}"
    local updated="$3"
    
    cat <<EOF
{
  "status": "$status",
  "message": "$message",
  "updated": "$updated",
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

# 2. 🌟 优化 3: 仅检查组件，不擅自触发系统级 apt/yum 安装
if ! command -v curl &> /dev/null; then
    output_json "error" "系统缺少核心组件 curl，请运维人员手动安装后重试。" "false"
    exit 3
fi

# 3. MD5 处理 (保持不变)
get_md5() {
    if command -v md5sum &> /dev/null; then
        md5sum "$1" | awk '{print $1}'
    elif command -v md5 &> /dev/null; then
        md5 -q "$1"
    else
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

# 5. 下载远程文件
CURL_OPTS="-s -w %{http_code} --connect-timeout 10 -m 60"
if [ -n "$TOKEN" ]; then
    HTTP_STATUS=$(curl $CURL_OPTS -o "$TMP_FILE" -H "$AUTH_HEADER" -H "$ACCEPT_HEADER" "$URL")
else
    HTTP_STATUS=$(curl $CURL_OPTS -o "$TMP_FILE" "$URL")
fi

CURL_EXIT_CODE=$?

if [ $CURL_EXIT_CODE -ne 0 ]; then
    output_json "error" "curl 下载失败，网络错误代码: $CURL_EXIT_CODE" "false"
    exit 3
fi

# 6. 检查状态码
if [ "$HTTP_STATUS" != "200" ]; then
    output_json "error" "GitHub 请求失败，HTTP 状态码: $HTTP_STATUS" "false"
    exit 3
fi

# 7. 状态分离：初始化生成
if [ ! -f "$LOCAL_PATH" ]; then
    mkdir -p "$(dirname "$LOCAL_PATH")"
    mv "$TMP_FILE" "$LOCAL_PATH"
    if [ -x "$LOCAL_PATH" ] || [[ "$LOCAL_PATH" == *.sh ]]; then
        chmod +x "$LOCAL_PATH"
    fi
    output_json "success" "本地文件不存在，已成功创建初始版本文件。" "init"
    exit 0
fi

# 8. 状态分离：哈希对比
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
    # 这里即使不写 rm -f，顶部的 trap 也会在 exit 0 时自动清理
    output_json "success" "文件无变化，本地已经是最新版本。" "false"
    exit 0
fi