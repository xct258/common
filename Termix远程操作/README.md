# Termix 远程操作

用一条命令操作 [Termix](https://github.com/Termix-SSH/Termix) 里登记的服务器：列主机、跑命令、拿退出码。
命令走 Termix 面板的 SSH 终端通道，内网机器不需要在本地暴露端口，也不需要本机保存私钥。

## 文件

| 文件 | 说明 |
| --- | --- |
| `termix` | Node CLI 本体（单文件，除 `ws` 外无第三方依赖） |
| `install.sh` | 安装到 `~/.config/opencode/bin/` 并软链到 `/usr/local/bin/termix` |
| `package.json` | 元数据，可在本目录 `npm i -g .`（未发布到 npm） |
| [../AGENTS.md](../AGENTS.md) | 给 AI Agent 的操作约定（安全规则、环境注意事项） |

## 依赖

- Node.js 18+
- `ws` 模块：
  - Debian/Ubuntu：`apt install node-ws`
  - 或 `npm install ws`

## 安装

```bash
bash install.sh
```

手动安装等价于：

```bash
install -m 755 termix ~/.config/opencode/bin/termix
ln -sf ~/.config/opencode/bin/termix /usr/local/bin/termix
```

`install.sh` 默认不覆盖 `/usr/local/bin/termix` 上已有的非本工具文件；确认可覆盖时加 `--force`。
安装目录和软链位置可用 `TERMIX_BIN_DIR` / `TERMIX_LINK` 覆盖。

## 令牌

Termix 面板生成 API Key 后写入文件：

```bash
printf '%s' '你的APIKey' > ~/.termix-token
chmod 600 ~/.termix-token
```

也可以用环境变量 `TERMIX_TOKEN`，或用 `TERMIX_TOKEN_FILE` 指定别的路径。
**不要把令牌提交到仓库。**

## 用法

```bash
termix ls                                   # 列登记的主机
termix ls --json                            # 同上，输出 JSON（给脚本/Agent 用）
termix run <id|名称|IP> "<命令>"             # 已登记主机
termix run --cred <凭据id> <IP> "<命令>"      # 未登记主机（quick-connect，不写库）
termix run --trust <目标> "<命令>"            # 接受变更过的 host key
termix --help                               # 用法
```

示例：

```bash
termix run 3 'df -h -x tmpfs -x devtmpfs'
termix ls | grep pve
termix run pve-1 'qm list; pvesm status'
termix run --cred 2 192.168.50.200 'ubus call system board'
```

### 环境变量

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `TERMIX_TOKEN` | 读 `~/.termix-token` | 面板 API Key |
| `TERMIX_TOKEN_FILE` | `~/.termix-token` | 令牌文件路径 |
| `TERMIX_URL` | `https://termix.xct258.top` | 面板地址，支持 `http`/`https` 和子路径 |
| `TERMIX_TIMEOUT` | `60` | 单条命令超时（秒） |
| `TERMIX_TRUST` | 空 | `1`/`true` 时等价于始终加 `--trust` |
| `TERMIX_PORT` | `22` | `--cred` 模式的 SSH 端口 |
| `TERMIX_USER` | `root` | `--cred` 模式的用户名 |

命令通过完整 PTY 执行，支持多行、管道和 `&&`；本地退出码等于远端命令的退出码（归一化到 0–255）。

## 工作流程

每次调用都是独立连接，不做连接复用：

1. `POST /users/ws-ticket/api-key` 用 API Key 换一张 1 分钟有效的 WebSocket 票
2. 建立 `wss://.../plugin-ws/ssh-terminal/terminal`
3. 发送 `connectToHost`，由 Termix 服务端代取凭据并发起 SSH
4. 发送命令，用 `__TMX_BEGIN__` / `__TMX_END__:<code>` 标记切分输出和退出码
5. 关闭连接

标记下发时写成带引号的 `__TMX_"BEG"IN__`，由远端 shell 去掉引号后才变成真正的标记，
这样 PTY 回显的行和解析用的标记行不同，回显不会混进结果。

单次开销约 0.5–1.5 秒，主要花在握手和 SSH 登录。命令多时建议在一次调用里用 `;`/`&&` 串起来，比分多次调用快。

### host key

- 首次连接：自动接受，仅本次生效，不写库
- 已记录的 key 发生变化：默认拒绝并打印新旧指纹，确认无误后加 `--trust`（或 `TERMIX_TRUST=1`）重试

### 交互式认证

遇到 `password_required`、`passphrase_required`、`totp_required` 会直接报错退出（`需要交互式认证`），不会卡住等输入。

### 异常情况

- 远端执行 `exit` / `reboot` / 网络断开导致连接先于结束标记关闭：报「连接在命令结束前关闭」，并回显最后收到的输出，退出码 1。
- 单条命令输出超过 32MB：直接报错，避免把内存吃满。
- 远端默认 shell 需要是 POSIX shell（sh/bash/zsh/fish 的 POSIX 语法）。

## 注意事项

- API Key 只能操作 Termix 里已授权的内容，拿不到凭据明文（`/credentials/:id/secret` 返回 404），密码由服务端在建立终端时填充。
- `--cred` 走 quick-connect，不会在面板里新增主机条目；返回的密码/私钥只存在进程内存，不会打印。
- 所有连接都会在 Termix 留下审计日志，操作按管理员身份计算。
- 命令里如果包含 `__TMX_BEGIN__` / `__TMX_END__` 字面量会被拒绝（会和输出标记冲突）。
- 这是「一条命令一个连接」的轻量封装；需要保留 shell 状态或高频调用时，再考虑常驻守护进程方案。
