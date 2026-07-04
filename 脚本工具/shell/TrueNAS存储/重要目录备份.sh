#!/bin/bash

# ============================================================
# 重要目录备份脚本
# 说明：从统一配置文件读取参数，然后按配置备份目录。
# ============================================================

# 7z 加密密码（可在此修改）
BACKUP_PASSWORD="your_backup_password"

# 1) 通过调度脚本传递基目录参数，子脚本使用该路径读取配置文件
dispatcher_dir="${1:-}"
if [ -z "$dispatcher_dir" ]; then
    echo "请在调用子脚本时传递调度脚本目录，例如: /root/apps/脚本/备份" >&2
    exit 1
fi

config_file="$dispatcher_dir/备份配置.conf"
if [ ! -f "$config_file" ]; then
    echo "配置文件未找到: $config_file" >&2
    exit 1
fi

# shellcheck source=/dev/null
source "$config_file"

# ------------------------------------------------------------
# 配置变量由调度脚本 source 读取后传递，以下为业务执行逻辑
# ------------------------------------------------------------

# 创建临时备份目录
mkdir -p "$backup_cache_dir"

# 获取当前时间，并格式化为 YYYY-MM-DD_HH:MM 的形式
backup_start_time_1=$(date +%Y/%m/%d/%H:%M:%S)

# 初始化一个空数组，用于存储生成的备份文件路径
backup_cache_file_paths=()
success=true  # 初始化成功标志

# 遍历需要备份的目录数组
for backup_dir_cache_1 in "${backup_directories[@]}"; do
    # 生成备份文件名，包含当前时间戳
    backup_cache_file_name="$backup_cache_dir/$(basename "$backup_dir_cache_1").7z"

    # 使用 tar 命令备份目录
    echo "正在压缩目录: $backup_dir_cache_1 到 $backup_cache_file_name"
    7z a -t7z -mhe=on -p"$BACKUP_PASSWORD" "$backup_cache_file_name" "$backup_dir_cache_1" > /dev/null 2>&1

    if [ $? -eq 0 ]; then
        echo "备份 ${backup_dir_cache_1} 目录成功"
        backup_cache_file_paths+=("$backup_cache_file_name")
    else
        echo "备份 ${backup_dir_cache_1} 目录失败"
        success=false
    fi
done

# 检查当前日期是否等于预设的备份日期
if [ "$(date +%d)" -eq $backup_dir_folder_time ]; then
    backup_dirs+=("${backup_dir_folder}")
    echo "今天是备份存档日期，已添加备份存档目录: ${backup_dir_folder}"
fi

# 为每个备份目标目录创建一个以当前时间命名的子目录
for backup_dir in "${backup_dirs[@]}"; do
    mkdir -p "${backup_dir}/${backup_start_time_1}"
    if [ $? -eq 0 ]; then
        echo "已创建备份目标子目录: ${backup_dir}/${backup_start_time_1}"
    else
        echo "创建备份目标子目录失败: ${backup_dir}/${backup_start_time_1}"
        success=false
    fi
done

# 将临时备份文件复制到每个备份目标目录
for backup_dir_cache_2 in "${backup_cache_file_paths[@]}"; do
    for backup_dir in "${backup_dirs[@]}"; do
        echo "正在复制备份文件: ${backup_dir_cache_2} 到 ${backup_dir}/${backup_start_time_1}/"
        cp "${backup_dir_cache_2}" "${backup_dir}/${backup_start_time_1}/"
        if [ $? -eq 0 ]; then
            echo "复制${backup_dir_cache_2}到目录${backup_dir}/${backup_start_time_1}成功"
        else
            echo "复制${backup_dir_cache_2}到目录${backup_dir}/${backup_start_time_1}失败"
            success=false
        fi
    done
done

# 删除临时备份文件目录及其中的所有内容
rm -rf "$backup_cache_dir"
echo "临时备份目录及其内容已删除: $backup_cache_dir"

# 清理超过30天的备份文件夹
for backup_dir in "${backup_dirs[@]}"; do
    to_delete=$(find "$backup_dir" -mindepth 4 -type d | sort | head -n -30)

    if [ -n "$to_delete" ]; then
        echo "$to_delete" | xargs rm -r
        if [ $? -eq 0 ]; then
            echo "已删除 ${backup_dir} 目录中超过30个备份的备份文件夹："
            echo "$to_delete"
        else
            echo "删除${backup_dir}目录中的旧备份失败"
            success=false
        fi
    else
        echo "${backup_dir} 中没有需要删除的目录"
    fi
done

# 输出最终的执行结果并写入INI文件
if [ "$success" = true ]; then
    echo "所有操作成功完成！"
    status="成功"
else
    echo "某些操作执行失败，请检查日志。"
    status="失败"
fi

# 写入INI文件
#{
#    echo "[重要目录备份]"
#    echo "时间 = \"$(date +%Y-%m-%d_%H:%M:%S)\""
#    echo "状态 = \"$status\""
#} > "$ini_file"

#echo "备份状态已写入到 $ini_file"