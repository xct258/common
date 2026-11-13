# 其他配置

## rc.local

`rc.local` — 系统开机自启脚本。

在 `exit 0` 之前插入需要开机执行的命令：

```bash
#!/bin/sh -e
# 在此处添加开机启动命令

exit 0
```

确保 rc.local 可执行：

```bash
chmod +x /etc/rc.local
systemctl enable --now rc-local
```
