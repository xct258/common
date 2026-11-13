# Docker 容器配置

各类 Docker 容器化服务的 docker-compose 配置文件、启动命令及说明文档。

## 附属文档

| 文件 | 说明 |
|------|------|
| `docs/docker容器打包离线安装.txt` | docker save / docker load 离线打包与导入方法 |
| `docs/alist美化配置-自定义头部.txt` | Alist 文件列表的自定义 CSS 美化样式 |
| `maintenance/` | 批量拉取 / 清理镜像脚本 |

## 服务列表

| 目录 | 服务 | 端口 | 说明 |
|------|------|------|------|
| `3-xui/` | 3X-UI 代理面板 | 2053 | vless+websocket 代理管理面板 |
| `adguardhome/` | AdGuard Home | 3001 / 3002 | DNS 去广告与域名解析 |
| `aria2和qb/` | aria2 + qBittorrent | 6880 / 9080 | BT 与直链双下载引擎 |
| `cloudflared-tunnel/` | Cloudflare Tunnel | - | 内网穿透 |
| `docker构建debian带novnc桌面系统的镜像xfce/` | Debian Xfce 桌面 | 6901 | 浏览器访问 Linux 桌面 |
| `esphome/` | ESPHome | 6052 | 智能设备固件管理 |
| `flare/` | Flare 导航页 | 5005 | 自部署书签导航 |
| `homeassistant/` | Home Assistant | - | 智能家居中枢 |
| `jellyfin/` | Jellyfin | 8096 | 媒体服务器 |
| `nginx-proxy-manager/` | NPM | 81 | 反向代理管理 |
| `opencode/` | opencode | - | AI 编程助手容器 |
| `syncthing/` | Syncthing | 8384 | 多设备文件同步 |
| `vaultwarden/` | Vaultwarden | 8808 | 自托管密码管理器 |
| `webtop/` | WebTop | 3300 | 浏览器内 KDE 桌面 |
| `yourls/` | YOURLS | 8500 | 自托管短链接 |
