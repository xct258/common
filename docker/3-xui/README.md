# 3X-UI 代理面板

轻量级多协议代理管理面板，基于 Xray 内核。

## 部署信息

- **镜像**: `ghcr.io/mhsanaei/3x-ui:latest`
- **网络模式**: host（直接使用宿主机网络）
- **管理端口**: 2053
- **默认账号密码**: admin / admin

## 代理配置

- **协议**: vless
- **传输方式**: websocket
- **路径**: `/xct258`
- **证书**: 通过 Nginx 反向代理自动申请 Let's Encrypt SSL

## 数据持久化

| 宿主机路径 | 容器路径 | 说明 |
|-----------|---------|------|
| `./db/` | `/etc/x-ui/` | 数据库与配置 |
| `./cert/` | `/root/cert/` | 证书文件 |

## Nginx 反代配置参考

```nginx
location /xct258 {
    proxy_redirect off;
    proxy_pass http://127.0.0.1:37354;
    
    # 基础代理配置
    proxy_http_version 1.1;
    proxy_set_header Host $host;  # 优化：使用更标准的 $host
    
    # WebSocket 支持优化
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade"; 
    
    # 传递真实客户端 IP
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    
    # 性能与超时优化
    proxy_connect_timeout 60s;  # 新增：建立连接的超时时间
    proxy_send_timeout 60s;     # 新增：发送数据的超时时间
    proxy_read_timeout 1h;      # 优化：WebSocket 属于长连接，建议调大以防断连
    
    # 流量与缓存优化（针对代理节点非常关键）
    proxy_buffering off;        # 新增：关闭缓存，让流量实时转发，降低延迟和内存占用
}
```
