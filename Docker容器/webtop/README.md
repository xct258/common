# WebTop 网页桌面环境

基于 LinuxServer.io 的 WebTop 镜像，在浏览器中运行完整的 KDE 桌面环境。

## 部署信息

- **镜像**: `lscr.io/linuxserver/webtop:debian-kde`
- **网络模式**: host
- **共享内存**: 1GB

## 端口

| 端口 | 协议 | 说明 |
|------|------|------|
| 3300 | HTTP | Web 桌面访问 |
| 3301 | HTTPS | Web 桌面加密访问 |

## 环境变量

| 变量 | 值 | 说明 |
|------|-----|------|
| `CUSTOM_USER` | xct258 | 登录用户名 |
| `PASSWORD` | (已配置) | 登录密码 |
| `TITLE` | o1-2 | 页面标题 |
| `LC_ALL` | zh_CN.UTF-8 | 中文环境 |

## 数据卷

| 宿主机路径 | 容器路径 | 说明 |
|-----------|---------|------|
| `/home/xct258/webtop共享文件夹` | `/config` | 桌面配置与文件 |
| `/usr/bin/rclone` | `/usr/bin/rclone` | 云盘挂载工具 |
| `rclone.conf` | `/root/.config/rclone/rclone.conf:ro` | rclone 配置（只读） |

## 使用

浏览器访问 `http://<IP>:3300` 即可进入 KDE 桌面，支持文件管理、终端、浏览器等常见操作。
