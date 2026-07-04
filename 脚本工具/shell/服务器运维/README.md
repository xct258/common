# 服务器运维 Shell 脚本

服务器自动化运维脚本集合。

## 脚本列表

| 脚本 | 说明 |
|------|------|
| `debian服务器开机执行脚本.sh` | Debian 系统开机自启执行 |
| `log.sh` | 日志记录工具函数库 |
| `rclone相关.sh` | rclone 云盘目录存档与自动清理 |
| `ssh登录提醒.sh` | SSH 登录时推送 Telegram 通知 |
| `开机挂载网络存储并启动相应的服务.sh` | 开机自动挂载 CIFS/OneDrive 并启动容器 |
| `开机挂载网络存储.sh` | 仅挂载网络存储 |
| `服务器备份监控脚本.sh` | 服务器综合备份监控 |
| `自动更新cf优选ip.sh` | Cloudflare CDN IP 优选测速与 DNS 自动更新 |
| `自动更新ipv6解析.sh` | 自动更新 IPv6 DNS 解析 |
| `自动更新github文件.sh` | 检查 GitHub 仓库文件更新并自动覆盖本地 |
| `自动生成日志函数.sh` | 自动日志记录函数库 |
| `更新github文件.sh` | 手动更新 GitHub 文件 |

### 子目录

| 目录 | 说明 |
|------|------|
| `自动更新github文件/` | 自动更新 GitHub 文件的配套脚本 |

## 推送通知

脚本统一通过 Message Pusher API 发送 Telegram 通知，格式：

```bash
curl -s -X POST "https://msgpusher.xct258.top/push/root" \
  -d "title=标题&description=描述&channel=通知渠道&content=消息内容"
```
