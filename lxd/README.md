# LXD 容器管理

LXD 系统容器创建与管理命令记录。

## 常用命令

| 命令 | 说明 |
|------|------|
| `lxc list` | 列出所有容器 |
| `lxc launch images:debian/13 debian13-1 -n ens18 -c security.privileged=true` | 创建 Debian 13 特权容器 |
| `lxc config set debian13-1 security.nesting true` | 启用嵌套虚拟化（Docker 支持） |
| `lxc exec debian13-1 -- /bin/bash` | 进入容器 |
| `lxc delete debian13-1 --force` | 强制删除容器 |
| `ip link set eth0 down && ip link set eth0 up` | 容器内重启网络 |

## 一键安装 Docker

```bash
curl -fsSL https://get.docker.com | bash -s docker
```
