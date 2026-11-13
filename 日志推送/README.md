# 日志记录和推送

通用日志函数库及 MQTT 消息推送脚本。

## 脚本

| 脚本 | 说明 |
|------|------|
| `log.sh` | Bash 日志模块，支持分级日志、文件管理、MQTT 推送 |

## log.sh 使用

`log.sh` 是一个 Bash 日志函数库，需通过 `source` 引入。

### 基本用法

```bash
source log.sh
log info "服务已启动"
log success "操作完成"
log error "发生错误"
```

### 日志级别

| 级别 | 符号 | 说明 |
|------|------|------|
| `debug` | 🐞 | 调试信息 |
| `success` | 🎉 | 成功消息 |
| `info` | ✅ | 常规信息 |
| `warn` | ⚠️ | 警告 |
| `error` | ❌ | 错误 |
| `fatal` | 💀 | 致命错误 |

### MQTT 推送

```bash
log_push "deploy/status" "部署完成" "success"
log_push_config api_url "http://10.0.0.1:8383/api/push"
```

### 环境变量

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `LOG_APP_NAME` | 脚本名 | 日志子目录名 |
| `LOG_BASE_DIR` | `./logs/` | 日志存储根目录 |
| `LOG_MAX_FILES` | 30 | 保留最大文件数 |
| `LOG_MQTT_API_URL` | `http://127.0.0.1:8383/api/push` | MQTT 推送 API |
