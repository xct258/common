#!/usr/bin/env bash
# 安装 termix CLI 到用户目录，并软链到 /usr/local/bin
set -euo pipefail

SRC_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
BIN_DIR=${TERMIX_BIN_DIR:-$HOME/.config/opencode/bin}
LINK=${TERMIX_LINK:-/usr/local/bin/termix}
UPLOAD_LINK=${TERMIX_UPLOAD_LINK:-/usr/local/bin/termix-upload}
FORCE=0

for a in "$@"; do
  case "$a" in
    -f | --force) FORCE=1 ;;
    -h | --help)
      cat <<EOF
用法: bash install.sh [--force]

  --force   覆盖已存在的软链（非本工具安装的文件默认不覆盖）

环境变量:
  TERMIX_BIN_DIR       安装目录（默认 \$HOME/.config/opencode/bin）
  TERMIX_LINK          termix 软链位置（默认 /usr/local/bin/termix）
  TERMIX_UPLOAD_LINK   termix-upload 软链位置（默认 /usr/local/bin/termix-upload）
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
install -m 755 "$SRC_DIR/termix-upload" "$BIN_DIR/termix-upload"
echo "已安装: $BIN_DIR/termix"
echo "已安装: $BIN_DIR/termix-upload"

link_bin() {
  local target="$1" link="$2" name="$3"
  local dir
  dir=$(dirname "$link")
  if [ -L "$link" ] && [ "$(readlink -f "$link" 2>/dev/null)" = "$(readlink -f "$target")" ]; then
    echo "已软链: $link -> $target"
    return 0
  fi
  if [ -e "$link" ] && [ "$FORCE" -ne 1 ]; then
    echo "跳过软链：$link 已存在且不是本工具的链接（$(readlink -f "$link" 2>/dev/null || echo 普通文件)）"
    echo "  确认可以覆盖后重跑：bash install.sh --force"
    return 1
  fi
  if [ -w "$dir" ]; then
    ln -sfn "$target" "$link"
  elif command -v sudo >/dev/null 2>&1; then
    sudo ln -sfn "$target" "$link"
  else
    echo "跳过软链（$dir 不可写且没有 sudo），可手动执行："
    echo "  ln -sfn $target $link"
    return 1
  fi
  echo "已软链: $link -> $target"
}

link_bin "$BIN_DIR/termix" "$LINK" termix || true
link_bin "$BIN_DIR/termix-upload" "$UPLOAD_LINK" termix-upload || true

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

if ! command -v curl >/dev/null 2>&1; then
  echo
  echo "缺少 curl（仅 termix upload 需要）："
  echo "  apt install curl"
fi

if [ ! -f "${TERMIX_TOKEN_FILE:-$HOME/.termix-token}" ] && [ -z "${TERMIX_TOKEN:-}" ]; then
  echo
  echo "还缺少令牌，请写入 Termix 面板生成的 API Key："
  echo "  printf '%s' '你的APIKey' > ~/.termix-token"
  echo "  chmod 600 ~/.termix-token"
fi

exit "$missing"
