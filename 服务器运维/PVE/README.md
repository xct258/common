# Proxmox VE 虚拟化

Proxmox VE 虚拟化平台配置记录。

## 硬件直通

```bash
# 移除 local-lvm，将空间合并到 root
lvremove pve/data
lvextend -l +100%FREE -r pve/root
# 数据中心 → 存储 → 删除 local-lvm

# 启用 IOMMU
nano /etc/default/grub
GRUB_CMDLINE_LINUX_DEFAULT="quiet intel_iommu=on iommu=pt"
update-grub
reboot
```

## 软件源配置

```bash
rm -rf /etc/apt/sources.list
rm -rf /etc/apt/sources.list.d/
```

然后在 Web 界面添加 No-Subscription 源。

## 内存盘挂载

```bash
mkdir -p /mnt/ramdisk-1
mount -t tmpfs -o size=32G tmpfs /mnt/ramdisk-1

# 开机自动挂载（/etc/fstab）
tmpfs /mnt/ramdisk-1 tmpfs defaults,size=32G 0 0
```
