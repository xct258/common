# common

个人自用的服务器运维与配置仓库，按**项目**组织：每个顶层目录对应一套服务/工具，内部包含自己的脚本、配置和说明。

> 仓库中的密码、令牌、API Key 均已替换为占位符，使用前请填入实际值。

## 项目索引

| 项目 | 说明 |
|---|---|
| [debian-init](debian-init/) | Debian 服务器初始化脚本（时区/locale/7z/Docker/ufw/开机任务/OCI 多 IP） |
| [oci-network](oci-network/) | 甲骨文云附加 IPv4 / IPv6 配置笔记 |
| [ipv6-ddns](ipv6-ddns/) | IPv6 动态 DNS 更新（Shell / PowerShell / Go 三种实现） |
| [cf-ip-optimizer](cf-ip-optimizer/) | Cloudflare 优选 IP 测速与 DNS 自动更新 |
| [pve](pve/) | Proxmox VE 配置笔记 |
| [openwrt](openwrt/) | OpenWrt 配置笔记 |
| [lxd](lxd/) | LXD 容器使用笔记 |
| [rc-local](rc-local/) | rc.local 开机启动配置 |
| [rclone](rclone/) | rclone 配置说明与辅助脚本 |
| [ssh-login-notify](ssh-login-notify/) | SSH 登录 Telegram 提醒 |
| [server-backup](server-backup/) | 服务器备份监控脚本 |
| [log-push](log-push/) | 日志记录与消息推送函数库 |
| [server-monitor](server-monitor/) | 服务监控面板（PHP） |
| [random-image-api](random-image-api/) | 随机图片 API |
| [web-templates](web-templates/) | 通用前端组件与样式模板 |
| [docker](docker/) | Docker 服务部署，一个服务一个目录 |
| [truenas](truenas/) | TrueNAS 配置、备份与硬盘休眠/空间监控 |
| [media](media/) | 媒体串流相关驱动与安装包 |
| [project-recorder](project-recorder/) | ProjectRecorder 桌面工具（.NET） |
| [server-reinstall](server-reinstall/) | 服务器网络重装笔记 |
| [github-file-sync](github-file-sync/) | GitHub 文件自动更新脚本 |

## 约定

- 每个项目目录内自带 README，说明用途、部署方式和注意事项
- 安装包/驱动等二进制保留在对应项目目录（未使用 LFS）
- 脚本默认只依赖系统自带工具；涉及第三方依赖的会在项目 README 里标注
