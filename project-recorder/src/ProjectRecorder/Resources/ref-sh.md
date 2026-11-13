# Shell（Bash）中文速查（详细版）

> 面向日常脚本编写，Bash / POSIX 常用语法、参数展开、文本处理与常见坑。

## 目录
- [脚本基础](#脚本基础)
- [变量与引号](#变量与引号)
- [参数展开详解](#参数展开详解)
- [算术运算](#算术运算)
- [条件判断](#条件判断)
- [测试表达式](#测试表达式)
- [循环](#循环)
- [函数](#函数)
- [数组](#数组)
- [字符串操作](#字符串操作)
- [IFS 与读取](#ifs-与读取)
- [重定向与管道](#重定向与管道)
- [子 shell 与进程替换](#子-shell-与进程替换)
- [通配与查找](#通配与查找)
- [文本处理：grep / sed / awk](#文本处理grep--sed--awk)
- [进程与作业](#进程与作业)
- [trap 与信号](#trap-与信号)
- [参数解析 getopts](#参数解析-getopts)
- [特殊变量](#特殊变量)
- [常用命令](#常用命令)
- [一行命令集锦](#一行命令集锦)
- [调试与常见坑](#调试与常见坑)

## 脚本基础

```bash
#!/usr/bin/env bash
# 第一行 shebang，指定解释器
set -euo pipefail   # 出错退出；未定义变量报错；管道任一失败即失败
IFS=$'\n\t'         # 收紧默认分隔符（可选，慎用）
```

- 执行：`chmod +x s.sh` 后 `./s.sh`，或 `bash s.sh`、`source s.sh`（当前 shell 执行）。
- 调试：`bash -x s.sh` 打印执行过程；脚本内 `set -x` / `set +x` 开关。
- 严格模式：`set -e`（出错退出）、`set -u`（未定义变量报错）、`set -o pipefail`。
- 兼容 POSIX：用 `#!/bin/sh`，避免 Bash 专有语法（`[[ ]]`、数组、`${var^^}` 等）。

## 变量与引号

```bash
name="Tom"          # 赋值：= 两侧不能有空格
echo "$name"
readonly PI=3.14    # 只读
unset name
export PATH="$PATH:/opt/bin"   # 导出为环境变量
declare -i n=0      # 整型
declare -r x=1      # 只读
```

引号规则：
- 单引号 `'...'`：完全原样，不能包含单引号。
- 双引号 `"..."`：变量、命令替换 `$(...)`、算术 `$((...))` 会展开；`\` `"` `$` 需转义。
- 无引号：会做单词拆分与通配展开，容易出错，尽量避免。
- 反引号 `` `cmd` `` 等同 `$(cmd)`，但不易嵌套，推荐 `$()`。

```bash
echo '$name'        # 字面 $name
echo "${name}abc"   # 花括号界定边界
echo "共 $(ls | wc -l) 个"
printf "%s\t%d\n" "$name" 20   # printf 更可控
```

## 参数展开详解

```bash
${var}              # 基本
${#var}             # 长度
${var:-默认}         # 未设置或为空 -> 默认
${var-默认}          # 未设置（可为空）-> 默认
${var:=默认}         # 未设置或为空 -> 赋值并返回
${var:?错误信息}     # 未设置或为空 -> 报错退出
${var:+替代}         # 已设置且非空 -> 替代
${var:offset}       # 子串（从 offset）
${var:offset:len}   # 子串（长度 len）
${var#pattern}      # 删最短前缀
${var##pattern}     # 删最长前缀
${var%pattern}      # 删最短后缀
${var%%pattern}     # 删最长后缀
${var/pat/repl}     # 替换第一个
${var//pat/repl}    # 替换全部
${var/#pat/repl}    # 仅替换开头
${var/%pat/repl}    # 仅替换结尾
${var^^}            # 全大写
${var,,}            # 全小写
${!name}            # 间接引用：变量 name 的值再作为变量名
${!prefix@}         # 所有以 prefix 开头的变量名
${var@Q}            # 加上 shell 引用后输出（Bash 4.4+）
```

## 算术运算

```bash
a=3; b=4
echo $((a + b))          # 整数：+ - * / % ** 
((a++)); ((a+=2))
let "c = a + b"
echo "scale=2; 10/3" | bc        # 浮点
awk 'BEGIN{printf "%.2f\n", 10/3}'   # 浮点（无需 bc）
printf "%.2f\n" "$(echo "10/3" | bc -l)"
```

`$((...))` 内可直接用变量名（不用 `$`）：`$((a*b))`。

## 条件判断

```bash
if [ "$a" -gt 5 ]; then
    echo ">5"
elif [ "$a" -eq 5 ]; then echo "=5"
else echo "<5"; fi

# [[ ]] 是 Bash 增强：支持 && || 与正则，不做单词拆分
if [[ "$name" == T* && "$a" -lt 10 ]]; then echo yes; fi
if [[ "$line" =~ ^[0-9]+$ ]]; then echo "纯数字：${BASH_REMATCH[0]}"; fi

case "$1" in
    start|up)   echo start ;;
    stop)       echo stop ;;
    *.tar.gz)   echo archive ;;
    *)          echo unknown ;;
esac
```

注意：`[` 是命令，`]` 前要有空格；两边变量务必加引号 `"$a"`。

## 测试表达式

| 类别 | 表达式 |
|------|--------|
| 文件 | `-e` 存在、`-f` 普通文件、`-d` 目录、`-r/-w/-x` 权限、`-s` 非空、`-L` 链接、`-nt/-ot` 更新/更旧 |
| 字符串 | `-z` 空、`-n` 非空、`=`/`==`、`!=`、`<` `>`（`[[ ]]` 内） |
| 数值 | `-eq -ne -lt -le -gt -ge` |
| 逻辑 | `!`、`-a`/`-o`（`[ ]`）或 `&&`/`||`（`[[ ]]`） |

```bash
[ -f f ] && echo 存在
[ ! -d d ] || mkdir -p d
[[ -n "${VAR:-}" ]] && echo "已设置"
```

## 循环

```bash
for i in 1 2 3; do echo "$i"; done
for ((i=0;i<5;i++)); do echo "$i"; done
for f in *.txt; do echo "$f"; done
for x in "$@"; do echo "$x"; done          # 参数，引号保留空格

while read -r line; do echo "$line"; done < file.txt
while IFS=, read -r a b c; do echo "$a|$b|$c"; done < data.csv

i=0; until [ "$i" -ge 3 ]; do echo "$i"; ((i++)); done

while :; do
  ...
  break      # 跳出循环
  continue   # 进入下一轮
done

# C 风格 + 同时读文件
while IFS= read -r l; do echo "$l"; done < <(grep foo file)
```

## 函数

```bash
greet() {
    local who="${1:-世界}"     # local 局部变量
    echo "你好，$who"
    return 0                   # 返回状态码 0-255
}
greet 朋友

# 返回字符串：echo + 命令替换
upper() { echo "${1^^}"; }
s=$(upper abc)                 # -> ABC

# 通过变量间接返回
setvar() { local -n ref=$1; ref=$2; }
setvar out "值"; echo "$out"

# 导出函数给子进程
export -f greet
```

## 数组

```bash
arr=("a" "b c" "d")
echo "${arr[0]}"; echo "${arr[@]}"; echo "${#arr[@]}"
arr+=("e")                    # 追加
for x in "${arr[@]}"; do echo "$x"; done
echo "${arr[@]:1:2}"          # 切片：从索引1取2个
unset 'arr[1]'

declare -A map
map[name]="Tom"; map[age]=20
echo "${map[name]}"
for k in "${!map[@]}"; do echo "$k=${map[$k]}"; done
[[ -v map[name] ]] && echo "存在"   # Bash 4.2+
```

## 字符串操作

```bash
s="Hello World"
echo "${#s}"              # 11
echo "${s:0:5}"           # Hello
echo "${s/World/Shell}"   # 替换一次
echo "${s//o/0}"          # 全部替换
echo "${s^^}"; echo "${s,,}"
echo "${s#Hello }"        # World（删前缀）
echo "${s%World}"         # "Hello "（删后缀）
```

## IFS 与读取

```bash
IFS=',' read -ra parts <<< "a,b,c"      # 拆成数组
echo "${parts[1]}"                      # b

# 逐字段读取 CSV（保留行内空格）
while IFS=, read -r name age city; do
  printf '%s(%s)-%s\n' "$name" "$age" "$city"
done < data.csv

# 读文件时 IFS 用换行，保留前导空白
while IFS= read -r line; do echo "$line"; done < f.txt
```

## 重定向与管道

```bash
cmd > out           # stdout 覆盖
cmd >> out          # 追加
cmd 2> err          # stderr
cmd &> all          # stdout+stderr
cmd > out 2>&1      # 等价
cmd < in            # stdin
cmd1 | cmd2         # 管道
cmd | tee out       # 同时打印与写文件
exec 3>file; echo hi >&3; exec 3>&-   # 自定义文件描述符

# heredoc
cat <<EOF > f
变量展开：$name
EOF
cat <<'EOF' >> f    # 不展开
$name 原样
EOF

# here-string
grep foo <<< "hello foo bar"

# 丢弃输出
cmd >/dev/null 2>&1
```

## 子 shell 与进程替换

```bash
( cd /tmp && ls )      # 子 shell，不影响当前目录
{ cd /tmp; ls; }       # 当前 shell 执行（注意空格和分号）

# 进程替换：把命令输出当作文件
diff <(sort a) <(sort b)
while read -r l; do echo "$l"; done < <(find . -name '*.log')

# 命名管道
mkfifo /tmp/p; producer > /tmp/p & consumer < /tmp/p
```

## 通配与查找

```bash
*.txt        # 结尾
?            # 单字符
[abc] [a-z]  # 字符集
{a,b}.log    # 展开 -> a.log b.log
**/*.py      # 递归（shopt -s globstar）
!(*.o)       # 扩展通配（shopt -s extglob）

find . -name "*.log" -mtime +7 -delete
find . -type f -size +10M
find . -name "*.tmp" -exec rm {} \;
find . -type f -print0 | xargs -0 grep -l foo
find . -maxdepth 2 -type d
```

## 文本处理：grep / sed / awk

```bash
# grep
grep -rin "error" .            # 递归/忽略大小写/行号
grep -v debug app.log          # 反选
grep -E "a|b" file             # 扩展正则
grep -c pattern file           # 计数
grep -o "[0-9]\+" file         # 只输出匹配部分
grep -A2 -B2 pattern file      # 上下文

# sed
sed 's/old/new/' f             # 每行第一个
sed 's/old/new/g' f            # 每行全部
sed -i 's/a/b/g' f             # 原地
sed -n '10,20p' f              # 打印区间
sed '/^#/d' f                  # 删注释行
sed 's/^/    /' f              # 行首缩进
sed -E 's/(\w+)=(\w+)/\1: \2/' f  # 扩展正则与分组

# awk
awk '{print $1, $NF}' f                 # 首列、末列
awk -F',' '{sum+=$2} END{print sum}' csv
awk '$3 > 100 {print $1}' f             # 条件
awk 'BEGIN{FS=","; OFS="-"} {print $1,$2}'
awk 'NR==1 || /error/' f                # 首行或匹配
awk '{a[$1]++} END{for(k in a) print k,a[k]}' f   # 分组计数

sort f | uniq -c | sort -rn     # 词频
cut -d',' -f1,3 csv
tr 'a-z' 'A-Z' < f
column -t f                     # 对齐成列
```

## 进程与作业

```bash
cmd &                # 后台
jobs; fg %1; bg %1
nohup cmd >log 2>&1 &   # 退出终端仍运行
kill PID; kill -9 PID
pkill -f "pattern"
ps aux | grep nginx
pgrep -af nginx
timeout 5 cmd
wait                    # 等后台任务
xargs -P4 -n1 cmd       # 并发执行
```

## trap 与信号

```bash
trap 'echo 退出清理; rm -f "$tmp"' EXIT
trap 'echo 收到 Ctrl-C' INT
trap - EXIT            # 取消

tmp=$(mktemp)
# 信号：INT(2) TERM(15) EXIT(0) HUP(1) KILL(9，不可捕获)
```

## 参数解析 getopts

```bash
while getopts ":a:b:h" opt; do
  case "$opt" in
    a) A="$OPTARG" ;;
    b) B="$OPTARG" ;;
    h) usage; exit 0 ;;
    \?) echo "非法选项: -$OPTARG" >&2; exit 1 ;;
    :)  echo "选项 -$OPTARG 需要参数" >&2; exit 1 ;;
  esac
done
shift $((OPTIND - 1))    # 剩余位置参数
```

## 特殊变量

| 变量 | 含义 |
|------|------|
| `$0` | 脚本名 |
| `$1..$9`、`${10}` | 位置参数 |
| `$#` | 参数个数 |
| `$@` / `$*` | 所有参数（`"$@"` 推荐） |
| `$?` | 上条命令退出码（0 成功） |
| `$$` | 当前进程 PID |
| `$!` | 最后后台进程 PID |
| `$_` | 上条命令最后的参数 |
| `$LINENO` `$FUNCNAME` `$BASH_SOURCE` | 行号 / 函数名 / 脚本路径 |

取出脚本自身目录：`DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"`。

## 常用命令

`ls cd pwd mkdir rm cp mv touch ln cat less more head tail` · `chmod chown stat file` ·
`df du` · `tar gzip zip unzip` · `ps top kill pkill` · `curl wget` · `ssh scp rsync` ·
`date cal` · `echo printf read` · `which whereis type command -v` · `env export` ·
`history alias source` · `crontab systemctl journalctl` · `grep sed awk find xargs sort uniq cut tr wc tee`.

```bash
tar -czvf a.tar.gz dir/    # 打包
tar -xzvf a.tar.gz         # 解压
curl -fsSL url -o file
rsync -avz --delete src/ user@host:/dst/
df -h; du -sh */
```

## 一行命令集锦

```bash
# 统计目录下各扩展名数量
find . -type f | sed 's/.*\.//' | sort | uniq -c | sort -rn

# 批量重命名 .txt -> .md
for f in *.txt; do mv "$f" "${f%.txt}.md"; done

# 找最大文件
find . -type f -printf '%s %p\n' | sort -rn | head

# 删除空目录
find . -type d -empty -delete

# 监听文件变化
tail -f app.log | grep --line-buffered ERROR

# 端口占用
ss -ltnp | grep :8080  或  lsof -i :8080
```

## 调试与常见坑

- `[ "$a" = "b" ]`：中括号是命令，注意空格；变量加引号防空值/空格。
- 优先用 `[[ ]]`（Bash）；POSIX 只能用 `[ ]`。
- 命令失败判断：`if ! cmd; then ...`；`set -e` 下注意 `cmd || true`。
- `set -e` 陷阱：条件里的命令、`||` 左侧失败不会触发退出，属预期。
- `cd` 失败要处理：`cd "$dir" || exit 1`。
- 变量默认值用 `"${VAR:-默认}"`，避免 `set -u` 报错。
- 管道会隐式创建子 shell，管道内改变的变量不影响外层；用进程替换或 `lastpipe`。
- `$(...)` 会去掉末尾换行；需要保留用 `var=$(cmd; echo x)`。
- 文件名含空格：一律 `"$file"`；批量用 `find -print0 | xargs -0`。
- 算术比较用 `(( ))` 或 `[[ ]]`，字符串比较用 `[[ ]]`，别混用。
- `rm -rf "$dir/"` 前先校验 `dir` 非空：`[ -n "$dir" ] || exit 1`。
