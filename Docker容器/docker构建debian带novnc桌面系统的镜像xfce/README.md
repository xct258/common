# Debian + noVNC 桌面 Docker 镜像

构建带有 Xfce 桌面环境的 Docker 镜像，通过浏览器访问完整的 Linux 桌面。

## 构建信息

| 架构 | 标签 |
|------|------|
| ARM64 | `xct258/debian-xfce:arm64` |
| AMD64 | `xct258/debian-xfce:amd64` |
| 多架构 | `xct258/debian-xfce:latest` |

## 构建命令

```bash
# 单架构构建
docker build --network=host -t xct258/debian-xfce:amd64 .
docker build --network=host -t xct258/debian-xfce:arm64 .

# 多架构构建（使用 buildx）
docker buildx create --name multiarchbuilder --use
docker buildx build --platform linux/amd64,linux/arm64 -t xct258/debian-xfce --push .
```

## 启动命令

```bash
# 端口映射模式
docker run --name debian-xfce --restart=always \
  -e VNC_PASSWORD=your_vnc_password \
  -v /home/xct258/debian-xfce共享文件夹:/home/xct258/debian-xfce共享文件夹 \
  -d -p 6901:6901 xct258/debian-xfce

# Host 网络模式
docker run --name debian-xfce --restart=always --net=host \
  -e VNC_PASSWORD=your_vnc_password \
  -v /home/xct258/debian-xfce共享文件夹:/home/xct258/debian-xfce共享文件夹 \
  -d xct258/debian-xfce
```

## 访问

启动后通过 `http://<IP>:6901` 访问，密码为 `VNC_PASSWORD` 环境变量的值。
