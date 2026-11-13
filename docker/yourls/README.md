# YOURLS 短链接服务

自托管的 URL 短链接服务（Your Own URL Shortener），支持自定义关键词、统计点击量。

## 部署信息

- **端口**: 127.0.0.1:8500

## 数据库配置

| 参数 | 值 |
|------|-----|
| 数据库地址 | mysql |
| 数据库名 | yourls |
| 数据库用户 | xct258 |
| 数据库密码 |  |
| Root 密码 |  |

## 核心配置

| 环境变量 | 值 |
|---------|-----|
| `YOURLS_USER` | xct258 |
| `YOURLS_PASS` |  |
| `YOURLS_SITE` | https://yourls.xct258.top |
| `YOURLS_HOURS_OFFSET` | 8 |

## 数据卷

| 宿主机路径 | 容器路径 |
|-----------|---------|
| `./mysql/db/` | `/var/lib/mysql` |
| `./mysql/conf/` | `/etc/mysql/conf.d` |
| `./yourls_data/` | `/var/www/html` |

## MySQL 版本

使用 MySQL 9.2.0，配置参考 `mysql版本9.2.0` 文件。

## API 功能

YOURLS 提供 REST API，支持程序化生成短链接、批量操作和统计查询。
