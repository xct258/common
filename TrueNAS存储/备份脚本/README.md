# 备份脚本

基于 7z 压缩的目录备份脚本，支持定时调度、多目标备份和月度存档。

## 依赖

- **7z**：需提前安装并配置环境变量

## 配置

配置文件位于 `备份配置.conf`，主要参数：

| 参数 | 说明 |
|------|------|
| `schedule_sleep_time` | 调度时间（默认 `00:00`） |
| `backup_logs` | 日志存放目录 |
| `backup_directories` | 需要备份的源目录列表（如 docker 配置、图片等） |
| `backup_dirs` | 备份目标目录列表 |
| `backup_dir_folder` | 月度存档目录 |
| `backup_dir_folder_time` | 每月第几天执行存档备份 |
| `backup_cache_dir` | 临时文件存放目录 |
| `ini_file` | 服务监控配置文件路径 |

## 备份源

- `/mnt/ssd-1/xct258/docker` - Docker 容器配置
- `/mnt/ssd-1/xct258/图片` - 图片文件

## 备份目标

- `/mnt/hdd-1/重要备份` - 主要备份目的地
- `/mnt/backup-1/重要备份` - 月度存档目的地
