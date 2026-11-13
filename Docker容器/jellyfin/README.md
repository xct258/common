# Jellyfin 媒体服务器

开源媒体库管理系统，支持电影、电视剧、音乐、照片等媒体内容的整理与串流。

## 部署信息

- **镜像**: `jellyfin/jellyfin:latest`
- **端口**: 8096
- **时区**: Asia/Shanghai
- **用户**: PUID=1000, PGID=1000

## 数据卷

| 宿主机路径 | 容器路径 | 说明 |
|-----------|---------|------|
| `/home/xct258/docker/jellyfin/config` | `/config` | 配置、数据库、缓存 |
| `/home/xct258/网盘/onedrive/xct258/视频` | `/media:ro` | 媒体文件挂载（只读） |
| `/home/xct258/apps/字体` | `/字体:ro` | 自定义字体文件（字幕渲染） |

## 功能特点

- 硬件解码（需配置）
- 多用户支持
- 实时转码
- 客户端覆盖 TV、手机、Web
