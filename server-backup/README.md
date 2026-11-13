# server-backup

服务器综合备份与监控脚本：目录打包、上传网盘、空间与状态检查，并推送结果。

## 内容

- [服务器备份监控脚本.sh](服务器备份监控脚本.sh) — 主脚本
- 配置项（备份目录、可用时间、7z 密码、rclone 远端等）在脚本内的配置区或读取 `/root/apps/脚本/config/服务器基本信息.txt`

## 说明

- 依赖 `7z`（或 `7zz`）、`rclone`
- 建议由 `boot-exec` 或 systemd 定时任务调用
- 推送地址与密码均为占位符，使用前替换

相关项目：[truenas](../truenas/)（存储侧备份脚本）、[rclone](../rclone/)。
