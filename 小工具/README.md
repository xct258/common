# 小工具（ProjectRecorder）

.NET Framework 4.8 单文件桌面工具（约 120KB，Win10/11 自带运行环境，无需安装）：固定口令登录（输错 2 次退出）+ 软件内无操作 30 秒强制退出 + AES-256 加密本地存储。

## 1. 运行环境

- Windows 10 / 11（自带 .NET Framework 4.8，无需装任何运行时）
- 产物：`publish/小工具.exe`（单个文件，直接双击）

## 2. 编译发布（按步骤来，一定成功）

推荐在 **Windows** 上编译，一共 3 步：

### 第 1 步：安装 .NET SDK（仅编译用，装一次）

1. 打开 https://dotnet.microsoft.com/download ，下载 **.NET SDK 8.x 或 10.x（x64）**，一路“下一步”安装。
2. 打开新的 `cmd`（或 PowerShell），验证：
   ```
   dotnet --list-sdks
   ```
   能列出版本号（如 `8.0.xxx` / `10.0.xxx`）即表示安装成功。

> 说明：程序本身的目标框架是 `net48`（Win10/11 自带运行），SDK 只是借来做编译。Linux 上也能编译（见下），但编出来的 exe 只能在 Windows 运行。

### 第 2 步：获取源码

```bash
git clone <你的仓库地址>
cd <仓库目录>
```

或直接下载 ZIP 解压后进入目录（能看到 `ProjectRecorder.sln` 和 `src/` 即可）。

### 第 3 步：编译

**Windows**：双击 `publish.bat`，等窗口显示“发布完成”。

等价的手动命令（在仓库根目录执行）：

```
dotnet publish src/ProjectRecorder/ProjectRecorder.csproj -c Release -o publish
```

**Linux / macOS**（仅编译，exe 仍需到 Windows 运行）：

```bash
chmod +x publish.sh && ./publish.sh
```

### 第 4 步：验证

1. `publish/` 目录下有 `小工具.exe`（约 100+KB）。
2. 拷贝到 Windows 10/11 电脑，双击出现登录框即成功。
3. 输入口令 `xct258` 进入主界面（`项目管理` / `工作量统计`）。

### 编译常见问题

| 现象 | 原因 / 解法 |
|---|---|
| `MSB3644: 未找到 .NETFramework,Version=v4.8 的引用程序集` | 本机缺 .NET Framework 4.8 引用包。本项目已内置 `Microsoft.NETFramework.ReferenceAssemblies` 解决，需联网还原一次 NuGet（`dotnet restore` 会自动做）。内网机请先手动 `dotnet restore` 成功再 publish。 |
| Linux 下 `Couldn't find a valid ICU package` | 缺 ICU 运行库。按 `publish.sh` 写法设置 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` 即可绕过（仅编译不受影响）。 |
| `error CS0103` 之类代码报错 | 先 `git status` 确认源码完整，再贴完整报错排查。 |
| 双击 exe 提示缺 `.NET Framework` | 只会出现在 Win7/8 旧系统。Win10/11 自带 4.8+，无需处理。 |

## 3. 使用

- 固定口令：`xct258`（连错 2 次自动退出；口令即 AES 解密密钥）
- 主界面：`项目管理` / `工作量统计`
- 项目管理：项目卡片列表，右上角 `＋ 添加`（弹窗输入，添加后直接进入）；双击卡片进入，右键删除
- 项目详情：右上角 `＋ 添加工序`（弹窗只需工序名）；工序卡片双击进入，右键删除
- 工序详情：干净查看`第N步`文字（可选中复制）＋配图（点击放大）；右上角`＋ 添加操作步骤`（弹窗输文字、可插一张 ≤5MB 图片，存加密库）；右键步骤可排序/删除
- 工作量统计：`通用`固定为最后一个普通卡片，其余项目自动同步项目管理；点进项目后右上角`＋ 添加工序`，每张工序卡片 `− 数量 ＋ 添加` 快捷计数（日期默认班次日期）
- 班次：8:30~21:00 白班，其余夜班；0:00~8:30 的夜班归前一日（如 12 号 7 点记 11 号夜班）；卡片与汇总只显示本班累计
- 导出：工作量列表左上 `导 出` → 选日期 → 真 Excel（.xlsx，冻结标题行）
- 30 秒无操作（登录页＋主界面＋所有弹窗均计时）强制退出；状态栏/登录页实时倒计时

## 4. 数据与加密

- exe 同级 `Data/` 下：`projects.dat`（项目）/ `processes.dat`（工序＋步骤配图）/ `workload.dat`（工作量记录）/ `wprocesses.dat`（工作量工序），格式均为 `IV(16字节) + AES-256-CBC 密文`
- 密钥 = `SHA256(口令)`；内存 JSON 序列化后加密，绝不明文落盘；不向前兼容旧版 `.dat`

## 5. 改口令 / 改超时

- 口令：算新口令 SHA256 hex（`python3 -c "import hashlib; print(hashlib.sha256('新口令'.encode()).hexdigest())"`），替换 `Services/AuthService.cs` 中 `FixedPasswordHash`，旧 `.dat` 作废
- 超时：改 `Services/InactivityMonitor.cs` 中 `TimeoutSeconds`（当前 30）

## 6. 项目结构

```
ProjectRecorder.sln
publish.bat / publish.sh            一键发布（Win / Linux）
src/ProjectRecorder/
  App.xaml(.cs) / LoginWindow / MainWindow（主界面/项目/工序/步骤/工作量5视图同窗切换）
  AddProjectDialog / AddProcessDialog / StepDialog（含配图）/ ExportDateDialog / ImageViewerDialog
  Models/ProcessItem.cs(FlowStep) / WorkloadRecord.cs / WorkloadProcess.cs
  Services/AuthService.cs / CryptoService.cs / DataStore.cs / InactivityMonitor.cs / ExcelExporter.cs(手写xlsx)
  Converters/ImageConverters.cs
```

不进仓库的东西（本地生成，已在 `.gitignore` 忽略）：`bin/`、`obj/`、`.vs/`、`publish/` 下的 exe、运行时 `Data/*.dat`。

## 7. 上传到 GitHub

```bash
git init                    # 已有仓库可跳过
git add .
git status                  # 确认没有 bin/obj/.vs/exe/.dat 被加入
git commit -m "init: ProjectRecorder"
git branch -M main
git remote add origin <你的仓库地址>
git push -u origin main
```
