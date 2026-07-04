# aria2 + qBittorrent 双下载服务

同时提供 BT/PT 下载（qBittorrent）和 HTTP/直链下载（aria2）能力。

## qBittorrent

- **WebUI 端口**: 9080
- **镜像**: `superng6/qbittorrentee:latest`
- **下载目录**: `/home/xct258/下载/qb`
- **配置目录**: `./qb/配置文件`

## aria2

- **RPC 端口**: 6800
- **WebUI 端口**: 6880
- **镜像**: `superng6/aria2:webui-latest`
- **下载目录**: `/home/xct258/下载/aria2`
- **密钥**: 通过 `SECRET` 环境变量设置 RPC 密钥
- **缓存**: 512M
- **DHT 端口**: 57866

## 通用配置

- 均使用 **host 网络模式**
- 时区: `Asia/Shanghai`
- 自动重启
