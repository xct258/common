# TrueNAS 存储

TrueNAS 系统相关的配置、脚本和 Docker 容器管理文档。

## 根目录文件

| 文件 | 说明 |
|------|------|
| `docs/TrueNAS配置smb隐藏无权文件夹.txt` | SMB 共享中通过 ACL 隐藏用户无权限的子文件夹 |
| `备份配置.conf` | 备份脚本配置，定义源目录、目标目录、调度时间、日志路径等 |
| `docs/权限设置` | Linux ACL 权限管理速查（getfacl、chmod） |
| `docs/truenas实例组安装debian后设置中文环境` | Debian 容器/系统中配置中文 locale 的步骤 |

## 子目录

| 目录 | 说明 |
|------|------|
| `backup/` | 7z 压缩备份说明 |
| `scripts/` | 备份执行、硬盘休眠、空间检测等脚本 |
| `docker/` | TrueNAS 上运行的 Docker 容器配置 |

## 开机自启服务

通过 `/etc/rc.local` 配置开机启动以下服务：

| 服务 | 端口 | 说明 |
|------|------|------|
| `filebrowser` | 5470 | Web 文件管理器（管理 `/mnt`） |
| `openlist` | - | 开源列表服务 |
| 检测硬盘剩余空间 | - | 硬盘空间监控 |
| 检测网盘剩余空间 | - | 云盘空间监控 |
| 检测硬盘状态 | - | 硬盘健康监控 |
| 备份执行脚本 | - | 定时备份任务 |

### 启动命令参考

```bash
# filebrowser
setsid /root/apps/filebrowser/filebrowser \
  -a 0.0.0.0 -p 5470 \
  -d /root/apps/filebrowser/filebrowser.db \
  -r /mnt >/dev/null 2>&1 &

# openlist
setsid /root/apps/openlist/openlist server \
  --data /root/apps/openlist/data >/dev/null 2>&1 &
```
