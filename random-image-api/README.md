# 随机图片 API

随机返回一张图片，并自动提取图片主题色和文字推荐色，通过响应头返回给前端。

## 部署信息

- **镜像**: `php:apache`
- **端口**: 8181
- **容器名**: random-image

## 数据卷

| 宿主机路径 | 容器路径 |
|-----------|---------|
| `./var/www/html` | `/var/www/html` |
| `/mnt/ssd-1/shared/image/收藏/pc` | `/home/xct258/images/pc` |
| `/mnt/ssd-1/shared/image/收藏/pe` | `/home/xct258/images/pe` |

## API 使用

### 请求

```
GET /?type=pc|mobile
```

- `type=pc` 或 `type=desktop` — 返回 PC 端图片
- `type=pe` 或 `type=mobile` — 返回移动端图片
- 不传 `type` 时根据 User-Agent 自动判断设备类型

### 响应头

| 响应头 | 说明 |
|--------|------|
| `X-Theme-Color` | 主题色（RGB 格式） |
| `X-Theme-Color-Hex` | 主题色（HEX 格式） |
| `X-Text-Color` | 文字推荐色（RGB 格式） |
| `X-Text-Color-Hex` | 文字推荐色（HEX 格式） |
| `X-Text-Contrast` | 文字色与背景色对比度 |
| `X-Image-Id` | 图片唯一 ID |

### 排除重复图片

```
GET /?exclude=pc-image-1
```

## 功能特点

- 使用 ImageMagick 提取图片主色调，需在容器内安装：

```bash
docker exec -u 0 -it random-image bash -c "apt update && apt install -y imagemagick"
```

- 支持色彩缓存，避免重复计算
- Cookie 跟踪上次展示的图片，避免连续重复
- 图片 ID 自动分配与持久化
- WCAG AA 级对比度（≥4.5:1）的文字颜色推荐
