# debian-init

Debian 服务器初始化脚本，用于重装系统后一次性完成常用配置。

## 包含内容

- 时区 `Asia/Shanghai`、中文 locale（当前会话直接生效，无需重连）
- 基础软件、时间同步、内核符号链接清理
- 7zip（从 GitHub 官方仓库按架构下载并校验）
- Docker（官方源 3 次 → 阿里云镜像 → 失败退出，可选）
- ufw（自动探测 SSH 端口后放行 22/80/443）
- `boot-exec` 开机任务框架
- SSH 登录提醒（可选）
- OCI 附加 IPv4/IPv6（IPv6 从元数据自动读取，IPv4 手工录入，配置完成后打印各地址的出口公网 IP）

## 使用

```bash
chmod +x debian服务器开机执行脚本.sh
./debian服务器开机执行脚本.sh
```

必须以 root 运行。交互流程会依次询问是否安装 Docker、是否添加 SSH 提醒、是否配置 OCI 附加 IP。

非交互执行（管道、cloud-init）时所有询问使用默认值，不会卡住或直接退出。

## 输出文件

| 路径 | 说明 |
|---|---|
| `/var/log/boot-init.log` | 执行日志 |
| `/etc/oci-secondary-ips.conf` | OCI 附加 IP 配置 |
| `/usr/local/sbin/oci-secondary-ips` | 附加 IP 执行脚本 |
| `/etc/systemd/system/oci-secondary-ips.service` | 开机自动附加 IP |
| `/etc/systemd/system/boot-exec.service` | 开机任务服务 |
| `/etc/boot-exec/tasks.list` | 开机任务列表 |

## OCI 说明

OCI 元数据只提供主私有 IP 和 IPv6 列表，**辅助 IPv4 不会通过 DHCP 下发**，需要在 OCI 控制台分配后手工录入 CIDR。详见 [oci-network](../oci-network/)。
