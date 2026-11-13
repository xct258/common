#!/bin/bash

# 设置镜像文件存放的文件夹路径
image_folder="/root/apps/脚本/docker镜像/2025_03_22"

# 判断文件夹是否存在
if [ ! -d "$image_folder" ]; then
  echo "指定的文件夹 $image_folder 不存在！"
  exit 1
fi

# 循环遍历文件夹中的所有 .tar 文件
for tar_file in "$image_folder"/*.tar; do
  # 判断是否是 .tar 文件
  if [[ -f "$tar_file" ]]; then
    echo "导入镜像: $tar_file"
    
    # 使用 docker load 导入镜像
    docker load -i "$tar_file"
    
    if [ $? -eq 0 ]; then
      echo "镜像 $tar_file 导入成功"
    else
      echo "镜像 $tar_file 导入失败"
      exit 1
    fi
  else
    echo "没有找到打包的镜像文件"
  fi
done

echo "所有镜像处理完成！"
rm -rf $image_folder
