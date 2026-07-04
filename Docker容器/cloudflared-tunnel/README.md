# Cloudflare Tunnel 内网穿透

通过 Cloudflare Tunnel 将内网服务暴露到公网，无需公网 IP 和端口映射。

## 管理脚本

`cloudflared-tunnel管理脚本.sh` 提供一键管理功能：

- **首次运行**: 自动拉取最新镜像并创建隧道
- **日常运行**: 自动检测镜像更新，平滑升级容器
- **更新策略**: 比对本地与远程镜像 ID，有差异时自动重建容器

## 部署信息

- **镜像**: `cloudflare/cloudflared:latest`
- **网络模式**: host
- **容器名**: cloudflared-tunnel
- **运行方式**: `tunnel --no-autoupdate run --token ${TOKEN}`

## 配置

在脚本中修改 `TOKEN` 变量为你的 Cloudflare Tunnel Token：

```bash
TOKEN="你的隧道 Token"
```

> ⚠️ 注意：脚本中包含的 Token 仅为示例，使用前请替换为真实 Token。
