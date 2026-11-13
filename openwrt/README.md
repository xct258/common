# OpenWrt 软路由

基于 ImmortalWrt 的软路由配置，运行在 PVE 虚拟机中。

## PVE 导入镜像

```bash
qm importdisk 120 /var/lib/vz/template/iso/immortalwrt-24.10.0-rc4-x86-64-generic-ext4-combined-efi.img local
```

## 基础网络配置

| 步骤 | 命令/操作 |
|------|-----------|
| 修改 IP | `vi /etc/config/network` |
| 重启网络 | `/etc/init.d/network restart` |
| LAN 网关 | 设置为主路由 |
| DNS 服务器 | 自定义为 AdGuard Home |
| DHCP | 忽略 LAN 口 DHCP 服务 |
| DHCPv6 | 添加接口，设备为 @lan，防火墙与 LAN 相同 |

## 需要安装的软件包

- **snmpd** — SNMP 监控
- **openclash** — 代理客户端
- **zerotier** — 虚拟组网

## OpenClash 配置

- **模式设置**: 使用 fake-ip 模式
- **流量控制**: 绕过中国大陆
- **IPv6 设置**: 不允许 IPv6 流量代理，允许 IPv6 类型 DNS 解析
- **DNS 设置**: 追加并默认上游 DNS 服务器
  - NameServer: `8.8.8.8 tcp`
  - Default-NameServer: `8.8.8.8 tcp`

## ZeroTier 配置

- 开启自动客户端 NAT
- ZeroTier 官网添加路由表：Destination 为内网网段，Via 为 ZeroTier 虚拟 IP
- 新版本需勾选：允许管理 IP/路由、允许入站、允许转发、IP 动态伪装
- 防火墙 LAN 口必须开启 IP 动态伪装

## 主路由 Tag 分流

不同设备走不同网关，需配置 DHCP Tag：

编辑 `/etc/config/dhcp`：

```
config tag 'out'
    list dhcp_option '3,192.168.50.3'
    list dhcp_option '6,8.8.8.8'
    option force '1'
```

在管理界面静态地址分配中指定 tag 即可，重启 DHCP：

```bash
/etc/init.d/dnsmasq restart
```

## 流量监控

- [luci-app-bandix](https://github.com/timsaya/luci-app-bandix)
