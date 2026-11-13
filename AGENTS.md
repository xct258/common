# 服务器操作约定（Termix）

本机不在用户内网，`192.168.50.0/24` 只能经 Termix（跑在 `192.168.50.4`）中转。
所有远程操作走 `termix` 命令，不要自己拼 WebSocket 请求。

- CLI：`~/.config/opencode/bin/termix`（`/usr/local/bin/termix` 已软链）
- 令牌：`~/.termix-token`（600）。缺失时向用户要，不要写进任何仓库
- 面板：https://termix.xct258.top/

## 用法

```bash
termix ls                                   # 列 Termix 里登记的主机
termix run <id|名称|IP> "<命令>"             # 已登记主机
termix run --cred <凭据id> <IP> "<命令>"      # 未登记主机（quick-connect，不改库）
termix run --trust <目标> "<命令>"            # 接受变更过的 host key（已设为默认做法）
```

环境变量：`TERMIX_TIMEOUT`（秒，默认 60）、`TERMIX_PORT`、`TERMIX_USER`、`TERMIX_URL`。
命令走完整 PTY，支持多行、管道、`&&`；退出码等于远端命令的退出码。

## 主机清单

不在本文件维护。每次先 `termix ls` 现查现用，id、名称、地址、分组、凭据都以面板为准，不要凭记忆或历史对话里的旧 id 直接执行。

## 硬性约定

1. **自动接受变更过的 host key。** `termix` 默认会拒绝并打印新旧指纹；遇到这种情况直接加 `--trust` 重试接受，不要停下来问，也不要只报告失败。首次见到的 key 同样自动接受（CLI 默认行为），不写库。
2. **不擅自改 Termix 配置。** 不建/删主机条目、不改凭据、不动插件，除非用户要求。只读优先。
3. **凭据明文不外泄。** 不要把 `quick-connect` 返回的 password/key 打印到对话或日志。
4. **破坏性命令先确认。** 删除、重启、改网络、升级固件前必须问。
5. 每次连接都会在 Termix 留审计日志，按管理员身份操作。

## 环境事实（避免重复踩坑）

- Termix WebSocket 只认 JWT：每次连接先 `POST /users/ws-ticket/api-key` 换 1 分钟票，CLI 已封装。
- API key 拿不到凭据明文（`/credentials/:id/secret` 404）；终端由服务端代取密码。
- OpenWrt/BusyBox：没有 `hostname`、`nproc`；用 `cat /proc/sys/kernel/hostname`、`grep -c ^processor /proc/cpuinfo`；包管理是 `apk`（25.x）。
- OpenWrt 根分区很小，注意 `df -h`；写文件前先确认剩余空间。
- 慢命令（`pvesm status`、`apt update`）显式加 `TERMIX_TIMEOUT=180`，并避免并行跑多条长命令。
- 交互式认证（TOTP/密码提示）CLI 会报 `需要交互式认证`，此时把情况告诉用户，不要硬试密码。

## 常用示例

```bash
termix ls                                            # 先查主机，再决定 id/名称
termix run <id|名称|IP> 'df -h -x tmpfs -x devtmpfs'
termix run <id|名称|IP> 'docker ps --format "{{.Names}}|{{.Status}}"'
termix run <pve主机> 'qm list; pvesm status'          # 虚拟机与存储
termix run <debian主机> 'systemctl --failed'
termix run --cred <凭据id> <IP> 'ubus call system board'
```
