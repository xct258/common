# Docker 容器配置

各类 Docker 容器化服务的 docker-compose 配置文件、启动命令及说明文档。

## 根目录文件

| 文件 | 说明 |
|------|------|
| `docker启动命令.txt` | 常用容器的 docker run 命令速查（cloudflared、netdata、watchtower、samba、code-server、baidunetdisk 等） |
| `docker容器打包离线安装.txt` | docker save / docker load 离线打包与导入方法 |
| `alist美化配置-自定义头部.txt` | Alist 文件列表的自定义 CSS 美化样式（背景图、透明效果等） |

## 服务列表

| 目录 | 服务 | 端口 | 说明 |
|------|------|------|------|
| `3-xui/` | 3X-UI 代理面板 | 2053 | vless+websocket 代理管理面板 |
| `adguardhome/` | AdGuard Home | 3001 / 3002 | 双实例 DNS 去广告与域名解析 |
| `aria2和qb/` | aria2 + qBittorrent | 6880 / 9080 | 双下载引擎：BT 与 直链 |
| `cloudflared-tunnel/` | Cloudflare Tunnel | - | 内网穿透，免公网 IP |
| `docker构建debian带novnc桌面系统的镜像xfce/` | Debian Xfce 桌面 | 6901 | 浏览器中访问 Linux 桌面 |
| `esphome/` | ESPHome | 6052 | 智能设备固件管理与 OTA |
| `flare/` | Flare 导航页 | 5005 | 自部署的书签导航页 |
| `homeassistant/` | Home Assistant | - | 智能家居核心中枢 |
| `jellyfin/` | Jellyfin 媒体库 | 8096 | 影视音乐媒体服务器 |
| `message-pusher/` | 消息推送 | 3196 | 多渠道消息推送聚合 |
| `nginx/` | Nginx | 80 / 443 | 反向代理与 SSL 证书管理 |
| `nginx-proxy-manager/` | NPM | 81 | Web 界面管理反向代理 |
| `opencode/` | opencode 开发容器 | - | AI 编程助手服务 |
| `push-sever/` | 推送服务 | - | 自定义消息推送后端 |
| `syncthing/` | Syncthing | 8384 | 多设备文件实时同步 |
| `vaultwarden/` | Vaultwarden | 8808 | 自托管密码管理器 |
| `webtop/` | WebTop 桌面 | 3300 | 浏览器内 KDE 桌面环境 |
| `wordpress/` | WordPress | 8082 | 博客/CMS 系统 |
| `yourls/` | YOURLS | 8500 | 自托管短链接服务 |
