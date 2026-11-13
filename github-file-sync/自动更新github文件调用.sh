#!/bin/bash

# ==============================================================================
# 用途: 智能主控脚本 (安全防开/精细查杀 优化版)
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIG_FILE="$SCRIPT_DIR/config.ini"

# --- 0. 单例运行锁 (防多开) ---
LOCK_FILE="/tmp/github_monitor.lock"
# 使用文件描述符 9 打开锁文件，并尝试获取非阻塞排他锁
exec 9<> "$LOCK_FILE"
if ! flock -n 9; then
    echo -e "\033[31m[ERROR] 主控脚本已在运行中 (PID: $(cat $LOCK_FILE 2>/dev/null))，请勿重复启动！\033[0m"
    exit 1
fi
# 写入当前主进程 PID
echo $$ >&9

# --- 1. 信号捕捉 ---
trap 'echo -e "\n[INFO] 收到退出信号，释放锁文件，监控服务安全停止。"; exec 9>&-; rm -f "$LOCK_FILE"; exit 0' SIGINT SIGTERM

# --- 2. 自动创建区块化配置文件 ---
init_config() {
    if [ ! -f "$CONFIG_FILE" ]; then
        echo "[INFO] 配置文件 $CONFIG_FILE 不存在，正在自动创建带详尽注释的默认配置..."
        cat <<EOF > "$CONFIG_FILE"
# ==============================================================================
#                 GitHub 多文件自动更新与服务智能托管配置
# ==============================================================================
# 使用说明：
# 1. [#] 或 [;] 开头的行为注释行，脚本读取时会自动忽略。
# 2. 配置分为 [global] 全局配置块 和 多个 [task.任务名] 任务配置块。
# 3. 参数值两边不需要加引号，如有空格或特殊字符直接书写即可。
# ==============================================================================

[global]
# 【全局轮询间隔时间】
# 单位：秒。整轮检查（遍历完下方所有任务）结束后，主控脚本大休眠等待的时间。
LOOP_INTERVAL = 120

# 【任务间微休眠间隔】
# 单位：秒。每执行完【单个】任务的检查后，休眠多少秒再执行下一个任务。
# 作用：防止短时间内向 GitHub 发送大量并发请求导致被临时封禁或限流。
TASK_INTERVAL = 10

# 【核心更新脚本路径】
# 真正执行下载、MD5对比和文件覆盖的目标脚本路径。
UPDATE_SCRIPT = ./更新github文件.sh


# ==============================================================================
#                             任务配置列表 (Tasks)
# ==============================================================================
# 添加新任务模板（请删掉行首的 # 号使其生效）：
# [task.自定义任务英文名]
# LOCAL_PATH = 本地文件绝对路径（包含最终文件名）
# REPO       = GitHub仓库名（格式: 用户名/项目名）
# FILE_PATH  = 该文件在 GitHub 仓库内部的相对路径
# BRANCH     = 分支名称（可选，不写默认使用 main）
# TOKEN      = 私有仓库访问令牌（可选，公共仓库留空）
# START_CMD  = 纯启动命令（可选。若配置，当脚本更新后主控会自动 pkill 旧进程并重新启动它）
# ------------------------------------------------------------------------------

EOF
        echo -e "\033[33m[WARN] 默认配置文件已生成！请先编辑并配置任务后重新运行。\033[0m"
        exec 9>&-
        rm -f "$LOCK_FILE"
        exit 0
    fi
}

# --- 3. 核心：解析并执行任务 ---
run_tasks_from_ini() {
    local current_section=""
    local current_task_idx=0
    local t_local_path="" t_repo="" t_file_path="" t_branch="main" t_token="" t_start_cmd=""
    

# 🌟 必须补全的进程托管引擎
    manage_service_process() {
        local mode="$1"
        if [ -z "$t_start_cmd" ]; then return 0; fi

        local target_name=$(basename "$t_local_path")
        local target_dir=$(cd "$(dirname "$t_local_path")" && pwd)
        local cmd_first_word=$(echo "$t_start_cmd" | awk '{print $1}' | xargs basename)
        
        if [ "$mode" = "restart" ]; then
            echo -e "[进程管理] 正在通过内核 /proc 检索并清理旧进程: \033[33m$target_name\033[0m ..."
            local rogue_pids=""

            for pid_dir in /proc/[0-9]*; do
                [ -d "$pid_dir" ] || continue
                local pid=$(basename "$pid_dir")
                [ "$pid" -eq $$ ] && continue
                [ "$pid" -eq $PPID ] 2>/dev/null && continue

                if [ -L "$pid_dir/cwd" ] && [ "$(readlink "$pid_dir/cwd" 2>/dev/null)" = "$target_dir" ]; then
                    if [ -f "$pid_dir/cmdline" ]; then
                        local cmd_args=()
                        while IFS= read -r -d '' arg; do cmd_args+=("$arg"); done < "$pid_dir/cmdline" 2>/dev/null
                        # 🌟 核心突破：遍历该进程的所有启动参数，彻底无视 bash/nohup 等前缀解释器
                        for arg in "${cmd_args[@]}"; do
                            local b_arg=$(basename "$arg" 2>/dev/null)
                            if [ "$b_arg" = "$target_name" ] || [ "$b_arg" = "$cmd_first_word" ]; then
                                rogue_pids="$rogue_pids $pid"
                                break # 确认是目标进程，记录并跳出参数遍历
                            fi
                        done
                    fi
                fi
            done

            if [ -n "$rogue_pids" ]; then
                echo -e "  \033[31m-> [锁定目标] 发现残留 PID: ($rogue_pids)，发送安全退出信号...\033[0m"
                for p in $rogue_pids; do kill -15 "$p" 2>/dev/null || true; done
                
                echo -n "  -> [时序同步] 等待旧进程释放资源."
                local wait_count=0
                while true; do
                    local any_alive=0
                    for p in $rogue_pids; do [ -d "/proc/$p" ] && any_alive=1; done
                    if [ $any_alive -eq 0 ]; then
                        echo -e "\n  \033[32m-> [成功] 旧进程已确认安全死绝。\033[0m"
                        break
                    fi
                    if [ $wait_count -ge 15 ]; then
                        echo -e "\n  \033[41;37m -> [超时强杀] 执行 kill -9 强杀超度！ \033[0m"
                        for p in $rogue_pids; do kill -9 "$p" 2>/dev/null || true; done
                        sleep 0.5
                        break
                    fi
                    sleep 0.2; echo -n "."; wait_count=$((wait_count + 1))
                done
            fi
        fi

        # 完美时序拉新
        echo -e "  \033[32m-> [启动] 正在全新拉起业务服务...\033[0m"
        (cd "$target_dir" && nohup $t_start_cmd >> /dev/null 2>&1 &)
        echo -e "  \033[36m-> [成功] 服务已成功托管至系统后台。\033[0m"
    }


    # 内部函数：基于 PID 的智能精准进程托管 + 全局名称扫尾
    execute_current_task() {
        if [ -n "$t_local_path" ] && [ -n "$t_repo" ] && [ -n "$t_file_path" ]; then
            ((current_task_idx++))
            local task_name="${current_section#task.}"
            echo "[进度: $current_task_idx/$TOTAL_TASKS] 正在检查任务 [$task_name]"
            
            JSON_OUT=$("$UPDATE_SCRIPT" "$t_local_path" "$t_repo" "$t_file_path" "$t_token" "$t_branch")
            local exit_code=$?
            
            local message=$(echo "$JSON_OUT" | grep -o '"message": "[^"]*' | awk -F '"' '{print $4}')
            local updated=$(echo "$JSON_OUT" | grep -o '"updated": "[^"]*' | awk -F '"' '{print $4}')
            
            if [ $exit_code -eq 0 ]; then
                if [ "$updated" = "true" ]; then
                    echo -e "  \033[32m-> [状态: 文件更新] 成功从远程同步最新代码。\033[0m"
                    manage_service_process "restart"
                    
                elif [ "$updated" = "init" ]; then
                    echo -e "  \033[36m-> [状态: 初次部署] 脚本文件在本地首次落地生成。\033[0m"
                    manage_service_process "start"
                    
                else
                    # updated = "false"
                    if [ -z "$t_start_cmd" ]; then
                        echo "  -> [状态: 内容一致] 文件无变动，无需进程托管。"
                    else
                        echo "  -> [状态: 内容一致] 文件无变动，正在进行业务存活状态核验..."
                        
                        local is_alive=0
                        local target_name=$(basename "$t_local_path")
                        local target_dir=$(cd "$(dirname "$t_local_path")" && pwd)
                        
                        # 提前准备好纯粹的启动首字，用于精准匹配
                        local cmd_first_word=$(echo "$t_start_cmd" | awk '{print $1}' | xargs basename)
                        
                        for pid_dir in /proc/[0-9]*; do
                            [ -d "$pid_dir" ] || continue
                            local pid=$(basename "$pid_dir")
                            
                            # 严格自杀保护：跳过主控自身和父进程
                            [ "$pid" -eq $$ ] && continue
                            [ "$pid" -eq $PPID ] 2>/dev/null && continue

                            # 1. 验证运行路径
                            if [ -L "$pid_dir/cwd" ]; then
                                if [ "$(readlink "$pid_dir/cwd" 2>/dev/null)" = "$target_dir" ]; then
                                    # 2. 🌟 核心改进：严格的数组级参数精确比对
                                    if [ -f "$pid_dir/cmdline" ]; then
                                        # 利用 read -r -d '' 将 \0 分隔的内核参数安全地读入 Bash 局部数组中
                                        local cmd_args=()
                                        while IFS= read -r -d '' arg; do
                                            cmd_args+=("$arg")
                                        done < "$pid_dir/cmdline" 2>/dev/null
                                        
                                        # 🌟 核心突破：同步更新自愈判定的全数组扫描
                                        for arg in "${cmd_args[@]}"; do
                                            local b_arg=$(basename "$arg" 2>/dev/null)
                                            if [ "$b_arg" = "$target_name" ] || [ "$b_arg" = "$cmd_first_word" ]; then
                                                is_alive=1
                                                break 
                                            fi
                                        done
                                        if [ $is_alive -eq 1 ]; then break; fi # 已确认存活，跳出外层的 /proc 进程遍历循环
                                    fi
                                fi
                            fi
                        done
                        
                        # 判定自愈
                        if [ $is_alive -eq 1 ]; then
                            echo "  -> [状态: 保持最新] 业务进程在后台健康运行中，无需干预。"
                        else
                            echo -e "  \033[33m-> [触发自愈] 检测到后台业务进程缺失！正在冷启动将其拉活...\033[0m"
                            manage_service_process "start"
                        fi
                    fi
                fi
            else
                echo -e "  \033[31m-> [错误] 底层更新脚本执行报错! 原因: $message\033[0m"
            fi

            if [ $current_task_idx -lt $TOTAL_TASKS ] && [ ${TASK_INTERVAL:-0} -gt 0 ]; then
                echo "[INFO] 触发防限流微调控，休眠 $TASK_INTERVAL 秒..."
                sleep "$TASK_INTERVAL"
            fi
        fi
    }

    # 读取 INI 逻辑与原版保持完全一致...
    while IFS= read -r line || [ -n "$line" ]; do
        line=$(echo "$line" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')
        [[ -z "$line" || "$line" =~ ^# || "$line" =~ ^\; ]] && continue
        if [[ "$line" =~ ^\[(.*)\]$ ]]; then
            # 🌟 修复：必须在执行任何新的正则表达式前，先把当前匹配结果安全转移！
            local new_section="${BASH_REMATCH[1]}"
            
            if [[ "$current_section" =~ ^task\. ]]; then 
                execute_current_task
            fi
            
            # 使用转移后的安全变量进行赋值
            current_section="$new_section"
            t_local_path="" t_repo="" t_file_path="" t_branch="main" t_token="" t_start_cmd=""
            continue
        fi
        if [[ "$line" =~ ^([^=]+)=(.*)$ ]]; then
            local key=$(echo "${BASH_REMATCH[1]}" | sed 's/[[:space:]]*$//')
            local val=$(echo "${BASH_REMATCH[2]}" | sed 's/^[[:space:]]*//')
            val="${val%\"}" && val="${val#\"}"
            val="${val%\'}" && val="${val#\'}"
            if [ "$current_section" = "global" ]; then
                [ "$key" = "LOOP_INTERVAL" ] && LOOP_INTERVAL="$val"
                [ "$key" = "TASK_INTERVAL" ] && TASK_INTERVAL="$val"
                [ "$key" = "UPDATE_SCRIPT" ] && UPDATE_SCRIPT="$val"
            elif [[ "$current_section" =~ ^task\. ]]; then
                case "$key" in
                    LOCAL_PATH) t_local_path="$val" ;;
                    REPO)       t_repo="$val" ;;
                    FILE_PATH)  t_file_path="$val" ;;
                    BRANCH)     t_branch="${val:-main}" ;;
                    TOKEN)      t_token="$val" ;;
                    START_CMD)  t_start_cmd="$val" ;;
                esac
            fi
        fi
    done < "$CONFIG_FILE"
    if [[ "$current_section" =~ ^task\. ]]; then execute_current_task; fi
}

init_config

# --- 预检与主循环 (保持原版逻辑) ---
TASK_LIST=$(grep -E "^\[task\..*\]" "$CONFIG_FILE" | sed -E 's/\[task\.(.*)\]/\1/')
TOTAL_TASKS=$(echo "$TASK_LIST" | grep -c "^" | tr -d '[:space:]')
[ -z "$TASK_LIST" ] && TOTAL_TASKS=0
LOOP_INTERVAL=$(grep -E "^LOOP_INTERVAL" "$CONFIG_FILE" | awk -F '=' '{print $2}' | tr -d '[:space:]' | grep -v "^$" || echo 120)
TASK_INTERVAL=$(grep -E "^TASK_INTERVAL" "$CONFIG_FILE" | awk -F '=' '{print $2}' | tr -d '[:space:]' | grep -v "^$" || echo 0)
UPDATE_SCRIPT=$(grep -E "^UPDATE_SCRIPT" "$CONFIG_FILE" | awk -F '=' '{print $2}' | tr -d '[:space:]' | grep -v "^$" || echo "./更新github文件.sh")
[[ "$UPDATE_SCRIPT" != /* ]] && UPDATE_SCRIPT="$SCRIPT_DIR/$UPDATE_SCRIPT"

if [ ! -f "$UPDATE_SCRIPT" ]; then
    echo "[ERROR] 未找到底层的核心更新脚本: $UPDATE_SCRIPT"; exit 1
fi
chmod +x "$UPDATE_SCRIPT"

echo "[INFO] 成功加载智能托管版配置，进入多脚本监控状态..."

while true; do
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] ======= 开始新一轮轮询检查 ======="
    TASK_LIST=$(grep -E "^\[task\..*\]" "$CONFIG_FILE" | sed -E 's/\[task\.(.*)\]/\1/')
    TOTAL_TASKS=$(echo "$TASK_LIST" | grep -v "^$" | grep -c "^")
    
    if [ "$TOTAL_TASKS" -eq 0 ]; then
        echo -e "  \033[33m[WARN] 当前配置文件中未检测到任何有效任务！\033[0m"
    else
        echo -e "[预检通知] 本次共需检查 \033[36m$TOTAL_TASKS\033[0m 个脚本任务。"
        run_tasks_from_ini
    fi

    echo "[INFO] 全局轮询结束，将在 $LOOP_INTERVAL 秒后开始下一轮..."
    sleep "$LOOP_INTERVAL"
done
