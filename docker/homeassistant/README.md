# Home Assistant 智能家居

开源智能家居平台，集成各类 IoT 设备统一管理与自动化。

## 部署信息

- **镜像**: `ghcr.io/home-assistant/home-assistant:stable`
- **网络模式**: host（需发现局域网设备）
- **容器名**: homeassistant-1
- **时区**: Asia/Shanghai
- **特权模式**: 开启（支持蓝牙等设备接入）

## 数据卷

| 宿主机路径 | 容器路径 | 说明 |
|-----------|---------|------|
| `/home/xct258/docker/homeassistant-1/config` | `/config` | HA 配置与数据库 |
| `/run/dbus` | `/run/dbus:ro` | 蓝牙集成支持 |

## 访问

启动后通过 `http://<IP>:8123` 访问（host 模式默认端口）。
