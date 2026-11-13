# AdGuard Home DNS 去广告

双实例部署，分别为不同网段或用途提供 DNS 解析与广告过滤服务。

## 实例信息

| 实例 | Web 管理端口 | DNS 端口 |
|------|-------------|----------|
| adg-1 | 127.0.0.1:3001 | 53 (TCP+UDP) |
| adg-2 | 127.0.0.1:3002 | 54 (TCP+UDP) |

- 管理页面仅本地监听，需通过反向代理或 SSH 隧道访问
- DNS 端口对外暴露，作为网络 DNS 服务器使用

## 数据卷

| 宿主机路径 | 说明 |
|-----------|------|
| `/home/xct258/docker/adg-1/work` | adg-1 工作数据 |
| `/home/xct258/docker/adg-1/conf` | adg-1 配置文件 |
| `/home/xct258/docker/adg-2/work` | adg-2 工作数据 |
| `/home/xct258/docker/adg-2/conf` | adg-2 配置文件 |
