# 服务器重装脚本

基于 [bin456789/reinstall](https://github.com/bin456789/reinstall) 的一键网络重装系统脚本。

## 使用

```bash
# 下载脚本（国外服务器）
curl -O https://raw.githubusercontent.com/bin456789/reinstall/main/reinstall.sh

# 下载脚本（国内服务器）
curl -O https://cnb.cool/bin456789/reinstall/-/git/raw/main/reinstall.sh

# 安装 Debian 13
bash reinstall.sh debian 13
```

## 服务器密码对照（请替换为实际密码后使用）

| 服务器类型 | 密码 |
|-----------|------|
| 甲骨文云 | `your_oracle_password` |
| 亚马逊 AWS | `your_aws_password` |
| 谷歌云 GCP | `your_gcp_password` |
| 其他服务器 | `your_server_password` |
| 本地服务器 Debian 12 | `your_local_password` |
| 云服务器 Windows | `your_windows_password` |

## 附加：甲骨文云多 IP 配置

编辑 `/etc/network/interfaces`，在接口的 `iface` 段内添加：

```
up ip addr add 10.0.1.11/24 dev enp0s6
down ip addr del 10.0.1.11/24 dev enp0s6
```

重启网络：`systemctl restart networking`
