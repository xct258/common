# opencode 开发容器

基于 Debian 的 AI 编程助手运行容器，提供 opencode 服务。

## 部署信息

- **镜像**: `xct258/debian-cn:latest`
- **网络模式**: host
- **容器名**: opencode
- **重启策略**: always

## 数据卷

| 宿主机路径 | 容器路径 |
|-----------|---------|
| `/home/xct258/opencode` | `/home/xct258/opencode` |

## 管理脚本

`restart.sh` 提供一键重启与更新功能：

1. 进入目标目录
2. 停止并删除旧容器
3. 使用 `docker compose up -d` 重新创建
4. 进入容器安装/更新 opencode
5. 自动配置 API 密钥（通过 `restart.sh` 中的 `OPENCODE_KEY_FHL` 变量）
6. 后台启动 opencode web 服务

## 分流安装策略

| 场景 | 行为 |
|------|------|
| 配置了 API 密钥 | 完整安装：apt 工具 + Node.js + npm + ccnew 工具 |
| 未配置 API 密钥 | 极速安装：仅 apt 基础工具，跳过 npm 工具链 |
