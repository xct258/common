#!/usr/bin/env bash
# 安装 termix CLI 到用户目录，并软链到 /usr/local/bin
set -euo pipefail

SRC_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
BIN_DIR=${TERMIX_BIN_DIR:-$HOME/.config/opencode/bin}
LINK=${TERMIX_LINK:-/usr/local/bin/termix}
FORCE=0

for a in "$@"; do
  case "$a" in
    -f | --force) FORCE=1 ;;
    -h | --help)
      cat <<EOF
用法: bash install.sh [--force]

  --force   覆盖已存在的 $LINK（非本工具安装的文件默认不覆盖）

环境变量:
  TERMIX_BIN_DIR   安装目录（默认 \$HOME/.config/opencode/bin）
  TERMIX_LINK      软链位置（默认 /usr/local/bin/termix）
EOF
      exit 0
      ;;
    *)
      echo "未知参数: $a（--help 看用法）" >&2
      exit 2
      ;;
  esac
done

install -d -m 755 "$BIN_DIR"
install -m 755 "$SRC_DIR/termix" "$BIN_DIR/termix"
echo "已安装: $BIN_DIR/termix"

link_bin() {
  local dir
  dir=$(dirname "$LINK")
  if [ -w "$dir" ]; then
    ln -sfn "$BIN_DIR/termix" "$LINK"
  elif command -v sudo >/dev/null 2>&1; then
    sudo ln -sfn "$BIN_DIR/termix" "$LINK"
  else
    echo "跳过软链（$dir 不可写且没有 sudo），可手动执行："
    echo "  ln -sfn $BIN_DIR/termix $LINK"
    return 1
  fi
  echo "已软链: $LINK -> $BIN_DIR/termix"
}

if [ -L "$LINK" ] && [ "$(readlink -f "$LINK" 2>/dev/null)" = "$(readlink -f "$BIN_DIR/termix")" ]; then
  echo "已软链: $LINK -> $BIN_DIR/termix"
elif [ -e "$LINK" ] && [ "$FORCE" -ne 1 ]; then
  echo "跳过软链：$LINK 已存在且不是本工具的链接（$(readlink -f "$LINK" 2>/dev/null || echo 普通文件)）"
  echo "  确认可以覆盖后重跑：bash install.sh --force"
elif ! link_bin; then
  :
fi

# ---- 依赖检查 ----
missing=0
if ! command -v node >/dev/null 2>&1; then
  echo
  echo "缺少 Node.js（需要 18+）："
  echo "  apt install nodejs        # Debian/Ubuntu"
  missing=1
elif ! node -e 'process.exit(Number(process.versions.node.split(".")[0]) >= 18 ? 0 : 1)'; then
  echo
  echo "Node.js 版本过低（当前 $(node -v)），需要 18+"
  missing=1
fi

if ! node -e "require('ws')" >/dev/null 2>&1; then
  echo
  echo "缺少 ws 模块："
  echo "  apt install node-ws       # 或 npm install -g ws"
  missing=1
fi

if [ ! -f "${TERMIX_TOKEN_FILE:-$HOME/.termix-token}" ] && [ -z "${TERMIX_TOKEN:-}" ]; then
  echo
  echo "还缺少令牌，请写入 Termix 面板生成的 API Key："
  echo "  printf '%s' '你的APIKey' > ~/.termix-token"
  echo "  chmod 600 ~/.termix-token"
fi

exit "$missing"
