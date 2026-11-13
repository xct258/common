# SSH登录提醒

SSH 登录时向 **push-server** 推送一条服务通知。

## 内容

- [ssh登录提醒.sh](ssh登录提醒.sh) — 由 `/etc/profile.d/` 加载

## 使用

```bash
install -m 0644 ssh登录提醒.sh /etc/profile.d/ssh_login_notify.sh
```

## 推送配置

推送地址固定为：

```
https://push-server.o1-1.xct258.top/api/push
```

请求字段遵循 push-server 接口定义：

| 字段 | 值 |
|---|---|
| `server_name` | 配置文件的 `server_name`，没有配置时用 `hostname` |
| `category` | `service` |
| `topic` | `server/ssh-login` |
| `title` | `SSH 登录提醒` |
| `message` | Markdown 正文：时间、用户（含 sudo 来源）、来源 IP、服务器 IPv4/IPv6 |
| `status` | `info` |
| `push_to_mqtt` | `true` |
| `msg_type` | `markdown` |
| `mode` | `append` |

服务端会按 `category=service` 自动拼接 `【服务通知】`、`【服务】`、`【状态】` 头，因此脚本正文不写这些前缀。

## 行为

- 仅 SSH 会话触发（检测 `SSH_CONNECTION`）
- 按连接去重，`sudo -i` / 多次 login shell 不会重复推送
- 推送在后台子 shell 执行，不阻塞登录
- 日志：`/var/log/ssh-notify.log`
- 服务器名配置：`/root/apps/脚本/config/服务器基本信息.txt` 中的 `server_name=`（可选）
