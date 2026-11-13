# 媒体串流

游戏串流（Sunshine）相关工具和驱动文件。

## 文件清单

| 文件 | 说明 |
|------|------|
| `EVDsetup.exe` | EasyVirtualDisplay 虚拟显示器安装程序（Sunshine 官方版需要） |
| `SteamLinkAudioDrivers-main.zip` | Steam Link 虚拟音频驱动 |

## Sunshine 项目

| 版本 | 项目地址 | 说明 |
|------|---------|------|
| 官方版 | [LizardByte/Sunshine](https://github.com/LizardByte/Sunshine) | 多版本 Windows 支持，需额外安装虚拟显示器 |
| 地基版 | [qiin2333/Sunshine-Foundation](https://github.com/qiin2333/Sunshine-Foundation) | 自带虚拟显示器，仅支持最新版 Windows 11 |

## 常见问题

| 问题 | 解决方案 |
|------|---------|
| IPv6 无法连接 | 开启主路由的转发功能 |
| 串流无画面（PVE + VirtIO） | 关闭 VirtIO 网卡的 IPv6 UDP Segmentation Offload |

## SteamLinkAudioDrivers 安装

1. 解压 `SteamLinkAudioDrivers-main.zip`
2. 在解压目录打开终端，执行 `hdwwiz`
3. 选择对应的音频输出设备格式
