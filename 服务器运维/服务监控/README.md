# 服务监控面板

PHP 服务状态监控页面，读取 INI 配置文件展示各服务的运行状态。

## 部署

```bash
docker run --restart=always -d \
  -p 8182:80 \
  --name server-monitoring \
  -v /mnt/ssd-1/docker/server-monitoring:/var/www/html \
  php:apache
```

## 文件说明

| 文件 | 说明 |
|------|------|
| `index.php` | 主页面，读取 `配置文件/` 目录下的 INI 文件展示服务状态 |
| `style.css` | 页面样式 |
| `优化中.html` | 优化中的页面版本 |

## 功能

- 自动读取 INI 配置文件并解析为服务状态卡片
- 可折叠/展开的折叠面板（点击标题切换）
- 集成随机图片 API，自动提取主题色应用到页面主题
- 响应式设计

## INI 配置文件格式

INI 文件存放在 `配置文件/` 目录中，格式：

```ini
[服务名称]
key1 = value1
key2 = value2
```
