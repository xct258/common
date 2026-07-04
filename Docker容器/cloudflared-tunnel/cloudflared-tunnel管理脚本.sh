#!/bin/bash

# ================= 配置区域 =================
# 🌟 请把下面双引号里的 Token 换成你自己的真实 Token
TOKEN=""
# ============================================

CONTAINER_NAME="cloudflared-tunnel"
IMAGE_NAME="cloudflare/cloudflared:latest"

echo "=============================================="
echo "      Cloudflare Tunnel 智能检测升级脚本 (Host网络版) "
echo "=============================================="

# 1. 检查容器是否存在
if [ "$(docker ps -a -q -f name=^/${CONTAINER_NAME}$)" ]; then
    echo "🔄 检测到容器 [${CONTAINER_NAME}] 正在运行，开始比对云端版本..."

    # 获取本地正在运行的镜像 ID
    LOCAL_IMAGE_ID=$(docker inspect --format='{{.Image}}' ${CONTAINER_NAME})

    # 静默拉取远程最新镜像信息（不会影响当前运行的容器）
    echo "🔍 正在检查官方仓库是否有新版本..."
    docker pull -q ${IMAGE_NAME}

    # 获取刚刚拉取的远程最新镜像 ID
    REMOTE_IMAGE_ID=$(docker inspect --format='{{.Id}}' ${IMAGE_NAME})

    # 2. 比对本地和远程的镜像 ID
    if [ "$LOCAL_IMAGE_ID" = "$REMOTE_IMAGE_ID" ]; then
        echo "=============================================="
        echo "        🎉 已经是最新版本，无需更新！        "
        echo "=============================================="
        echo "保持当前隧道稳定运行，脚本优雅退出。"
        exit 0
    else
        echo "📢 侦测到官方发布了新版本！"
        echo "🛑 正在安全停止并删除旧版本容器..."
        docker rm -f ${CONTAINER_NAME}

        echo "🚀 正在使用 Host 网络和最新镜像重建并拉起隧道..."
        # 🌟 关键改动：加入了 --network host
        docker run -d \
          --name ${CONTAINER_NAME} \
          --network host \
          --restart=always \
          ${IMAGE_NAME} \
          tunnel --no-autoupdate run --token ${TOKEN}

        echo "✅ 隧道已成功平滑升级至官方最新版本！"
    fi
else
    echo "📥 检测到系统未安装 [${CONTAINER_NAME}]，开始全新安装..."

    echo "🚀 正在使用 Host 网络在后台拉起纯净版 Cloudflare 隧道..."
    # 🌟 关键改动：加入了 --network host
    docker run -d \
      --name ${CONTAINER_NAME} \
      --network host \
      --restart=always \
      ${IMAGE_NAME} \
      tunnel --no-autoupdate run --token ${TOKEN}

    echo "✅ 隧道已成功安装并设置为开机自启！"
fi

echo "----------------------------------------------"
echo "📊 当前容器最新的运行状态日志："
echo "----------------------------------------------"
sleep 1
docker logs --tail 10 ${CONTAINER_NAME}