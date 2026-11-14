# termix-upload

通过 Termix **File Manager 插件**的 HTTP API，把本地文件直接上传到 Termix 中登记的主机，不需要 PVE/NAS 中转。

## 用法

```sh
termix-upload <主机ID|名称|IP> <本地文件> [远端目录] [远端文件名]
```

示例：

```sh
termix-upload op-1 ./openwrt.img.gz /tmp
termix-upload 9 ./openwrt.img.gz /tmp openwrt.img.gz
```

默认远端目录是 `/tmp`，默认文件名取本地文件名。

## 依赖

- `curl`
- `node`
- `~/.termix-token`（与 `termix` 命令共用）
- 目标主机已启用 File Manager 功能
- 目标主机有 SFTP server：
  ```sh
  apk add openssh-sftp-server
  ```

如果没有 `openssh-sftp-server`，`/connect` 可能显示成功，但上传会报：

```text
SFTP error: Received exit code 127 while establishing SFTP session
```

## 接口

工具实际调用 Termix File Manager 插件：

```text
POST /plugin-api/file-manager/connect
POST /plugin-api/file-manager/uploadFileStream
POST /plugin-api/file-manager/disconnect
```

认证使用：

```http
Authorization: Bearer <Termix API Key>
```

连接时传入 `hostId`、`syncId`、`ip`、`port`、`username`，服务器端根据 host 记录解析 SSH 凭据，API Key 不需要拿到密码或私钥。

## 实现说明

1. 用 `termix ls --json` 找到主机 ID、IP、端口、用户名、syncId。
2. 生成随机 `sessionId`，调用 `/connect` 建立 SFTP 会话。
3. 用 multipart 上传：

```text
sessionId
path=<远端目录>
overwrite=true
file=@本地文件;filename=远端文件名
```

4. 退出时自动调用 `/disconnect` 清理会话。

## 速度与分块

大于 2MB 的文件会自动按 **512KB 分块**上传，避免单次请求超过 Cloudflare 的
100 秒超时（HTTP 524）。

实测 op-1：

```text
1MB  约 16 秒
5MB  约 85 秒
约 40～60KB/s
15MB 镜像约 6 分钟（29 块）
```

瓶颈在 **Termix 服务器 → op-1 的 SFTP/ZeroTier 链路**，不是本地文件读取或 API 本身。

同一目标走 PVE-1 的 HTTP 中转实测约：

```text
4.08MB/s（32.6Mbps）
```

因此：

- 小文件、临时文件：直接用 `termix-upload`
- 大镜像：PVE-1 HTTP 中转更快
- 走 Termix API 大文件也不会再被 524 打断，但耗时较长

## 注意

- 不要用本工具传敏感文件到不受信任的主机。
- 会话断开时会自动清理；上传中断可能留下部分文件，需要手动删除。
- 工具不会修改 Termix 的主机、凭据或插件配置。
