# IPv6 DDNS 更新器

Windows 系统上自动检测本机公网 IPv6 地址变化，通过 Cloudflare API 更新 DNS AAAA 记录。

## 使用方法

### 首次运行

以**管理员身份**运行 `ipv6_ddns.exe`，按提示录入 Cloudflare API Token、Zone ID 和域名，进入交互菜单。

### 菜单功能

```
========== DDNS 管理菜单 ==========
  域名:   ddns.example.com
  ZoneID: abcdef1234567890
  Token:  abcd****wxyz
  间隔:   10 分钟
  计划任务: 已创建
-----------------------------------
  1. 更新配置
  2. 查看配置
  3. 执行一次检查
  4. 删除计划任务
  5. 重新创建计划任务
  6. 退出
```

- **更新配置** — 修改 API Token、Zone ID、域名、检查间隔
- **执行一次检查** — 立即获取本机 IPv6 并与 Cloudflare 同步
- **创建计划任务** — 注册 Windows 计划任务，按间隔分钟数自动后台运行（使用 `--once` 模式触发）
- **删除计划任务** — 移除已注册的计划任务

### 命令行参数

| 参数 | 说明 |
|------|------|
| `（无参数）` | 以管理员身份运行，进入交互管理菜单 |
| `--once` | 执行一次 IPv6 检查后退出（供计划任务调用） |

### 首次配置

程序首次运行时会自动创建 `ddns_config.json` 配置文件，或直接在菜单的"更新配置"中修改。

配置文件包含：

```json
{
  "ApiToken": "your_cloudflare_api_token",
  "ZoneId": "your_zone_id",
  "Domain": "ddns.yourdomain.com",
  "IntervalMin": 10
}
```

## 日志

每次检查结果追加写入 `ddns_run.log`（与 exe 同目录），仅记录有结果的条目，IP 无变化时不产生冗余日志。日志大小超过 512KB 时自动截断保留后半部分。

```
[2026-07-21 12:00:01] 无变化(已跳过) | 2408::xxx → 2408::xxx
[2026-07-21 12:10:01] 更新成功 | 2408::xxx → 2408::yyy
[2026-07-21 12:20:03] 新建成功 | 无(未发现记录) → 2408::zzz
```

## 编译

需要安装 [Go](https://go.dev/dl/)。双击 `build.bat` 或在目录下执行：

```bat
go build -ldflags="-s -w" -o ipv6_ddns.exe ipv6_ddns.go
```
