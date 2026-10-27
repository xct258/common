#!/usr/bin/env bash
# Linux / macOS 一键发布（仅编译用；在 Windows 上请双击 publish.bat）
# 产物：publish/小工具Lite.exe（需拷贝到 Windows 10/11 上双击运行）
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet publish src/ProjectRecorderLite/ProjectRecorderLite.csproj -c Release -o publish
# Win10/11 自带 .NET Framework 4.8，config/pdb 不需要，删掉保持单文件
rm -f publish/*.config publish/*.pdb
echo
echo "发布完成：publish/小工具Lite.exe"
echo "数据：直接写入 exe 内部（明文 JSON，单文件）"
