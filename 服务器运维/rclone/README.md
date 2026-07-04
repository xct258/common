# rclone 云盘挂载

## OneDrive（私有 API）

使用 Azure 注册的私有应用挂载 OneDrive，比公共 API 更稳定。

### Azure 注册步骤

1. 登录 [Azure Portal](https://portal.azure.com)（E5 管理员账号）
2. 搜索 "App registrations" → 新注册
3. 填写：
   - 名称：`rclone-onedrive`
   - 支持账户类型：任何组织目录和个人 Microsoft 账户
   - 重定向 URI（Web）：`http://localhost:53682/`
4. 记录：应用程序(客户端)ID、目录(租户)ID
5. 进入"证书和密码"→ 新建客户端密码
6. 进入"API 权限"→ 添加委托权限：`User.Read`、`Files.ReadWrite.All`、`offline_access`
7. 点击"管理员同意"

### 服务器配置

```bash
rclone config
```

选择 OneDrive (38)，填入 Client ID、Client Secret、租户 ID，自动浏览器授权。

### 生成 Token

本地机器执行（需同版本 rclone）：

```bash
rclone authorize "onedrive" \
  --client-id <Client ID> \
  --client-secret <Client Secret>
```

将输出的 JSON Token 粘贴到服务器配置中。

### 挂载

```bash
rclone mount onedrive: /mnt/onedrive --allow-other --vfs-cache-mode writes
```

## Azure 开发者账号

记录在 `2025-07-06.txt` 中。

## Cloudflare R2

配置在 `rclone.conf` 中，使用 S3 兼容协议：

| 参数 | 值 |
|------|-----|
| Provider | Cloudflare |
| Endpoint | `https://9769bf86b756a322ec60530718735483.r2.cloudflarestorage.com` |

## 多服务器共享建议

每台服务器单独授权，保持独立的 refresh token。
