# CF 优选 IP

Cloudflare 优选 IP 工具：测速筛选延迟最低的节点，并通过 API 自动更新 DNS 记录。

## 内容

- [自动更新cf优选ip.sh](自动更新cf优选ip.sh) — 测速 + 更新 Cloudflare DNS

## 使用

脚本内包含 Cloudflare API Token、域名、记录名等占位符，使用前替换为实际值。建议配置 cron 定期执行。

详细参数见脚本头部注释。
