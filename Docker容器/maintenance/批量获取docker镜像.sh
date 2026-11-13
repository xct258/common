#!/bin/bash

# 设置镜像名称和对应的自定义打包名称（如果没有自定义名称，则使用镜像名和标签）
declare -A image_map
image_map=(
  ["xhofe/alist:latest"]="alist"
  ["superng6/qbittorrentee:latest"]="qbittorrentee"
  ["xct258/debian-bililive"]="bililive"
  ["p3terx/ariang"]="ariang"
  ["p3terx/aria2-pro"]="aria2pro"
  ["php:apache"]="php"
  ["vaultwarden/server:latest"]="vaultwarden"
  ["soulteary/flare:latest"]="flare"
  ["mysql:9.2"]="mysql"
)

time=$(date +%Y_%m_%d)

# 设置输出文件目录
output_dir="./docker镜像/$time"

# 创建目录（如果不存在的话）
mkdir -p $output_dir

# 循环拉取镜像并打包
for image in "${!image_map[@]}"; do
  # 获取自定义打包名称
  custom_name="${image_map[$image]}"
  
  echo "拉取镜像: $image"
  docker pull $image

  # 获取镜像标签
  image_tag=$(echo $image | cut -d ':' -f 2)

  # 打包镜像为 tar 文件
  output_file="${output_dir}/${custom_name}.tar"
  echo "打包镜像到文件: $output_file"
  docker save -o $output_file $image

  echo "镜像 $image 已成功拉取并保存为 $output_file"
  docker rmi $image
  echo "镜像 $image 已成功删除"
done

echo "所有镜像拉取并打包完成！"
