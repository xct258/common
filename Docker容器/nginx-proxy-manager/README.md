# Nginx Proxy Manager

Web 界面管理 Nginx 反向代理、SSL 证书和访问控制，无需手动编辑配置文件。

## 部署信息

- **镜像**: `docker.io/jc21/nginx-proxy-manager:latest`
- **网络模式**: host（直接使用宿主机端口）
- **占用端口**: 80 (HTTP)、443 (HTTPS)、81 (管理后台)

## 数据卷

| 宿主机路径 | 容器路径 |
|-----------|---------|
| `./data` | `/data` |
| `./letsencrypt` | `/etc/letsencrypt` |

## 初始化

```bash
mkdir -p /home/xct258/docker/nginx-proxy-manager
cd /home/xct258/docker/nginx-proxy-manager
```

## 访问

- **管理后台**: `http://<IP>:81`
- **默认账号**: `admin@example.com`
- **默认密码**: `changeme`

> ⚠️ 首次登录后必须修改默认账号密码。

## 自定义页面

`自定义页面.html` 可用于 404/502 等错误页面的自定义样式。
