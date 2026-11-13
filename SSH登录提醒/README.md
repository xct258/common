# ssh-login-notify

SSH 登录时推送提醒到 Telegram（基于 Message Pusher API）。

## 内容

- [ssh登录提醒.sh](ssh登录提醒.sh) — 放入 `/etc/profile.d/` 或作为登录钩子

## 使用

```bash
install -m 0644 ssh登录提醒.sh /etc/profile.d/ssh_login_notify.sh
```

脚本内的推送地址、Token 为占位符，使用前替换。每次通过 SSH 登录会发送来源 IP、主机名和登录用户。
