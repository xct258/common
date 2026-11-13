# Bilibili 直播录制容器

基于 Debian 的 Bilibili 直播录制容器（khx-live），自动录制指定直播间并支持硬件编码。

## 部署信息

- **镜像**: `xct258/khx-live`
- **网络模式**: host
- **容器名**: khx-live

## 环境变量

| 变量 | 说明 |
|------|------|
| `XCT258_GITHUB_TOKEN` | GitHub Token（用于项目更新） |
| `Bililive_USER` | Bilibili 登录用户名 |
| `Bililive_PASS` | Bilibili 登录密码 |

## 硬件支持

- **设备映射**: `/dev/dri:/dev/dri` - 启用 Intel/AMD GPU 硬件编码加速

## 数据卷

| 宿主机路径 | 容器路径 | 说明 |
|-----------|---------|------|
| `/mnt/ssd-1/shared/bililive` | `/rec` | 录播文件保存目录 |

## 录制说明

登录 Bilibili 账号后可录制大会员限定的直播内容，支持自动检测直播状态并开始/停止录制。
