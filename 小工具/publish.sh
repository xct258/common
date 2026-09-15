#!/usr/bin/env bash
# Linux / macOS 一键发布（仅编译用；在 Windows 上请双击 publish.bat）
# 产物：publish/小工具.exe（需拷贝到 Windows 10/11 上双击运行）
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet publish src/ProjectRecorder/ProjectRecorder.csproj -c Release -o publish
rm -f publish/*.config publish/*.pdb
echo
echo "发布完成：publish/小工具.exe"
