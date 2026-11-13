#!/usr/bin/env bash
# 安装 termix CLI 到用户配置目录，并软链到 /usr/local/bin
set -euo pipefail

SRC_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
BIN_DIR="$HOME/.config/opencode/bin"
LINK=/usr/local/bin/termix

install -d -m 755 "$BIN_DIR"
install -m 755 "$SRC_DIR/termix" "$BIN_DIR/termix"
echo "已安装: $BIN_DIR/termix"

if [ -w /usr/local/bin ]; then
  ln -sf "$BIN_DIR/termix" "$LINK"
  echo "已软链: $LINK -> $BIN_DIR/termix"
elif command -v sudo >/dev/null 2>&1; then
  sudo ln -sf "$BIN_DIR/termix" "$LINK"
  echo "已软链: $LINK -> $BIN_DIR/termix"
else
  echo "跳过软链（没有写权限也没有 sudo），可手动执行："
  echo "  ln -sf $BIN_DIR/termix $LINK"
fi

if [ ! -f "$HOME/.termix-token" ]; then
  cat <<'EOF'

还缺少令牌，请写入 Termix 面板生成的 API Key：
  printf '%s' '你的APIKey' > ~/.termix-token
  chmod 600 ~/.termix-token
EOF
fi

if ! node -e "require('ws')" >/dev/null 2>&1; then
  echo
  echo "未找到 ws 模块，请安装后使用："
  echo "  apt install node-ws    # 或 npm install ws"
fi
