# 自动更新 IPv6 解析（PowerShell + Cloudflare）

PowerShell 脚本 `ipv6_ddns.ps1`，在 Windows 上自动检测本机公网 IPv6 地址并更新 Cloudflare DNS AAAA 记录。

## 功能

- 自动从 Windows 网卡获取公网 IPv6（通过路由器通告的前缀 + Link 后缀）
- 首次运行以管理员身份自动注册为 Windows 任务计划程序定时任务
- 检测 IPv6 变化后自动更新 Cloudflare DNS 记录
- 支持自动创建不存在的记录
- 自动清理过大的日志文件

## 配置

在脚本顶部修改变量：

```powershell
$ApiToken     = "你的 Cloudflare API Token"
$ZoneId       = "你的 Zone ID"
$Domain       = "域名"
$IntervalMin  = 10      # 检查间隔（分钟）
$MaxLogSizeMB = 2       # 日志文件最大体积（MB）
```

## 安装

1. 以**管理员身份**运行 PowerShell
2. 执行脚本：`powershell.exe -ExecutionPolicy Bypass -File "脚本路径"`
3. 脚本会自动创建计划任务，之后每 10 分钟静默运行
