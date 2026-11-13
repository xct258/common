# common

个人自用的服务器运维与配置仓库，按**项目**组织：每个顶层目录对应一套服务/工具，内部包含自己的脚本、配置和说明。

> 仓库中的密码、令牌、API Key 均已替换为占位符，使用前请填入实际值。

## 项目索引

| 项目 | 说明 |
|---|---|
| [Debian初始化](Debian初始化/) | Debian 服务器初始化脚本（时区/locale/7z/Docker/ufw/开机任务/OCI 多 IP） |
| [IPv6动态解析](IPv6动态解析/) | IPv6 动态 DNS 更新（Shell / PowerShell / Go 三种实现） |
| [CF优选IP](CF优选IP/) | Cloudflare 优选 IP 测速与 DNS 自动更新 |
| [pve](pve/) | Proxmox VE 配置笔记 |
| [openwrt](openwrt/) | OpenWrt 配置笔记 |
| [lxd](lxd/) | LXD 容器使用笔记 |
| [rc-local](rc-local/) | rc.local 开机启动配置 |
| [SSH登录提醒](SSH登录提醒/) | SSH 登录 Telegram 提醒 |
| [服务器备份](服务器备份/) | 服务器备份监控脚本 |
| [日志推送](日志推送/) | 日志记录与消息推送函数库 |
| [Docker容器](Docker容器/) | Docker 服务部署，一个服务一个目录 |
| [TrueNAS存储](TrueNAS存储/) | TrueNAS 配置、备份与硬盘休眠/空间监控 |
| [媒体串流](媒体串流/) | 媒体串流相关驱动与安装包 |
| [项目记录器](项目记录器/) | ProjectRecorder 桌面工具（.NET） |
| [GitHub文件同步](GitHub文件同步/) | GitHub 文件自动更新脚本 |

## 约定

- 每个项目目录内自带 README，说明用途、部署方式和注意事项
- 安装包/驱动等二进制保留在对应项目目录（未使用 LFS）
- 脚本默认只依赖系统自带工具；涉及第三方依赖的会在项目 README 里标注
