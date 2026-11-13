#!/bin/bash

set -e

# ================= 配置区域 =================
CONTAINER_NAME="opencode"
TARGET_DIR="/home/xct258/docker/opencode"
# 填入你固定的密钥；如果想跳过配置，直接留空即可（例如 OPENCODE_KEY_FHL=""）
OPENCODE_KEY_FHL=""
# ============================================

echo "=== 1. 进入目标目录 ==="
cd "$TARGET_DIR"

echo "=== 2. 安全检查并关闭已有容器 ==="
if docker ps -a --format '{{.Names}}' | grep -Eq "^${CONTAINER_NAME}$"; then
    echo "[检测] 发现已存在的容器 '${CONTAINER_NAME}'，正在执行安全关闭..."
    docker compose down
else
    echo "[跳过] 未检测到名为 '${CONTAINER_NAME}' 的容器，无需执行 down。"
fi

echo "=== 3. 使用 docker-compose 启动新容器 ==="
docker compose up -d

echo "=== 4. 执行容器内安装与配置 ==="
docker exec -i "$CONTAINER_NAME" /bin/bash << EOF
  export DEBIAN_FRONTEND=noninteractive
  
  # -----------------------------------------------------------------
  # 优化核心：根据密钥状态，智能化决定是否安装高耗时/大体积的 npm 工具链
  # -----------------------------------------------------------------
  if [ -n "$OPENCODE_KEY_FHL" ] && [ "$OPENCODE_KEY_FHL" != "这里填入你固定的密钥内容" ]; then
      echo "=== 4.1 [分流: 完整模式] 正在安装基础工具与 Node.js/NPM... ==="
      apt update && apt install curl wget git sudo nano procps npm -y
      
      echo "=== 4.2 全局安装 ccnew 工具 ==="
      npm i -g ccnew
  else
      echo "=== 4.1 [分流: 极速模式] 未设置 API，跳过高耗时工具链(npm)的安装 ==="
      apt update && apt install curl wget git sudo nano procps -y
  fi
  
  # 3. 运行通用的 opencode 编译安装
  echo "=== 4.3 编译安装 opencode ==="
  curl -fsSL https://opencode.ai/install | bash

  export PATH="\$HOME/.local/bin:\$PATH"
  if [ -f ~/.bashrc ]; then source ~/.bashrc || true; fi

  # 4. 自动化键盘交互核心
  if [ -n "$OPENCODE_KEY_FHL" ] && [ "$OPENCODE_KEY_FHL" != "这里填入你固定的密钥内容" ]; then
      echo "=== 4.4 正在通过智能按键流执行 ccnew fhl... ==="
      (sleep 2; printf " "; sleep 0.2; printf "\n"; sleep 1; printf "$OPENCODE_KEY_FHL\n") | ccnew fhl || true
      echo "=== 4.5 ccnew fhl 交互执行完毕 ==="
  else
      echo "=== 4.4 [跳过] 跳过 ccnew fhl 交互配置 ==="
  fi

  # 5. 重新固化一遍符号链接，防止命令位置发生漂移
  ln -sf /root/.local/bin/opencode /usr/local/bin/opencode || true
  ln -sf \$HOME/.local/bin/opencode /usr/local/bin/opencode || true

  echo "=== 5. 正在后台启动 opencode web 服务 ==="
  export PATH="/usr/local/bin:\$PATH"
  
  if command -v opencode &> /dev/null; then
      nohup opencode web --hostname 0.0.0.0 > opencode.log 2>&1 &
  else
      nohup /root/.local/bin/opencode web --hostname 0.0.0.0 > opencode.log 2>&1 &
  fi
  disown

  sleep 3

  echo "------------------------------------------------"
  echo "=== 6. 检测并显示服务运行状态与端口 ==="
  echo "------------------------------------------------"
  
  if [ -f "opencode.log" ]; then
      DETECTED_PORT=\$(grep -oE "([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}:[0-9]+|:[0-9]+|port [0-9]+)" opencode.log | grep -oE "[0-9]+" | head -n 1)
      
      if [ -n "\$DETECTED_PORT" ]; then
          echo "[成功] opencode web 服务已在容器后台成功运行！"
          echo "👉 容器内监听端口为: \$DETECTED_PORT"
          echo "💡 请检查您的 docker-compose.yml 文件，通过对应的宿主机映射端口进行访问。"
      else
          echo "⚠️ 服务看似已拉起，但未能从日志中自动解析出端口。"
          echo "📝 容器内当前最新日志如下："
          tail -n 5 opencode.log
      fi
  else
      echo "❌ [错误] 未能找到 opencode.log 日志文件，服务可能未成功运行！"
  fi
  echo "------------------------------------------------"
EOF

echo "=== 7. 脚本执行完毕，终端已释放！ ==="
