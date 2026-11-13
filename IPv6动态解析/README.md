# IPv6 动态解析

IPv6 动态 DNS（DDNS）更新工具，三种实现按平台分目录。

## 目录

| 目录 | 实现 | 适用平台 |
|---|---|---|
| [shell/](shell/) | Shell 脚本 | Linux（Debian/Ubuntu 等） |
| [powershell/](powershell/) | PowerShell | Windows |
| [go/](go/) | Go 源码 + 编译产物 | Windows / Linux，单文件运行 |

## 用途

定期读取本机 IPv6 地址，调用 DNS 服务商 API 更新 AAAA 记录，适用于家庭宽带 IPv6 前缀变化、服务器多 IPv6 地址等场景。

各实现的具体配置（域名、API Token、更新间隔）见对应目录内的 README。
