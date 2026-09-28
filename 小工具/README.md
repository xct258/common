# 小工具（ProjectRecorder）

.NET Framework 4.8 单文件桌面工具（约 180KB，Win10/11 自带运行环境，无需安装）：固定密码登录（输错 2 次退出）+ 软件内无操作 120 秒强制退出 + AES-256 加密本地存储。

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
cd <仓库目录>/小工具
```

或直接下载 ZIP 解压后进入目录（能看到 `ProjectRecorder.sln` 和 `src/` 即可）。

### 第 3 步：编译

**Windows**：双击 `publish.bat`，等窗口显示“发布完成”。

等价的手动命令（在 `小工具/` 目录执行）：

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
3. 输入密码 `xct258` 进入主界面：左侧为 `通用` 模块入口 + 项目列表，右侧为选中项的面板。

### 编译常见问题

| 现象 | 原因 / 解法 |
|---|---|
| `MSB3644: 未找到 .NETFramework,Version=v4.8 的引用程序集` | 本机缺 .NET Framework 4.8 引用包。本项目已内置 `Microsoft.NETFramework.ReferenceAssemblies` 解决，需联网还原一次 NuGet（`dotnet restore` 会自动做）。内网机请先手动 `dotnet restore` 成功再 publish。 |
| Linux 下 `Couldn't find a valid ICU package` | 缺 ICU 运行库。按 `publish.sh` 写法设置 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` 即可绕过（仅编译不受影响）。 |
| `error CS0103` 之类代码报错 | 先 `git status` 确认源码完整，再贴完整报错排查。 |
| 双击 exe 提示缺 `.NET Framework` | 只会出现在 Win7/8 旧系统。Win10/11 自带 4.8+，无需处理。 |

## 3. 使用

- 固定密码：`xct258`（不区分大小写；连错 2 次自动退出；密码即 AES 解密密钥）
- 保存登录：登录页勾选 `保存登录` 后，登录信息用 Windows DPAPI（仅当前 Windows 用户可解密）加密存到 `%APPDATA%\ProjectRecorder\auto-login.dat`；下次启动跳过登录框直接进入，同时取消 120 秒无操作自动退出（右下角显示“已记住登录”）；主界面右下角 `记住登录` 勾选框可随时取消（取消后恢复登录页与 120 秒自动退出）
- 主界面：左侧上方是独立的 `通用` 模块（顶部六个功能入口：`工作量` / `导 出` / `快捷路径` / `思维导图` / `笔 记` / `加密文件`），下方是项目列表（右键上移/下移/删除），左下 `＋ 添加项目`
- 选中项目后，右侧面板顶部切换 `工序` / `工作量` / `快捷路径`，无需逐级返回
- 工序页：左列工序列表（`＋ 添加工序`、右键上移/下移/删除）；点选工序后右列显示步骤
- 步骤：右上角 `＋ 添加操作步骤`（弹窗输文字、可插入多张图片：文件框可多选追加、单张 ≤5MB、缩略图点 × 移除）；点击配图放大（默认适应窗口，滚轮缩放，按住拖动平移，多图可上一张/下一张）；右键上移/下移/删除
- 工作量页：无需选日期，自动记当前班次（页首显示班次日期 + 白/夜班，右上角 `＋ 添加工序`）；卡片只统计**本班**数量，换班次（8:30 / 21:00 / 跨日）自动从 0 重新统计
- 每张工序卡片 `- 数量 +` 调数量（步进 `1 ↔ -1` 不经过 0，数量不能为 0）；`添 加` 记入当前班次，负数即冲减/纠正多记；本班数量最低为 0，冲减超过本班数量时直接按本班数量冲减到 0（无弹窗）
- 班次：8:30~21:00 白班，其余夜班；0:00~8:30 的夜班归前一日（如 12 号 7 点记 11 号夜班）
- 导出：`通用` → `导 出` 入口 → 选日期 → `导 出 Excel` → 真 Excel（.xlsx，冻结标题行，列宽按内容自动计算，只列各项目/工序的数量合计，同一项目的单元格纵向合并，不列明细）
- 快捷路径：`通用` 和每个项目各有一份，互不干扰（面板顶部都有 `快捷路径` 入口）；`通用` 左右分栏 `文件夹（单击打开）` / `网址（单击复制）`，右上角 `＋ 添加` 可选类型 `文件夹` / `网址`；项目里只显示 `文件夹` 一栏，`＋ 添加` 也只能添加文件夹。文件夹可“浏览…”选择、单击用资源管理器打开；网址自动补 `https://`、单击复制到剪贴板（失败自动打开手动复制弹窗，手动 Ctrl+C 兜底；不打开浏览器，标题行提示“已复制”），右键可复制网址/删除；删除项目会一并删除该项目的快捷路径
- 思维导图：`通用` 模块独立功能，全宽画布 + 顶部导图名下拉（切换/`＋ 新建`/重命名/删除；空列表时点导图名或页面中央按钮直接新建）——每个节点分 `标题` + `内容` 两部分，画布上只显示标题（鼠标悬停显示内容），双击节点弹窗编辑（标题必填；`Ctrl+Enter` 确定）、右键节点 `添加子节点` / `添加同级节点` / `编辑标题/内容` / `删除节点`、空白处右键给中心主题加子节点、空白拖动平移（或拖滚动条）、鼠标滚轮缩放（以鼠标位置为中心）、右上角 `－ / ＋ / 适应窗口` 调视图；键盘：`Tab` 加子节点、`Enter` 加同级（根节点为编辑）、`F2` 编辑、`Delete` 删除、方向键移动选择、`Esc` 取消选择；`操作教程` 按钮弹窗显示完整说明；`导 出 ▾` 菜单支持 PNG 图片（白底、2 倍分辨率，不受当前缩放影响）和 TXT 文本大纲（层级缩进输出标题与内容，适合粘贴给 AI 提需求）；增删改后自动加密保存
- 笔记：`通用` 模块独立功能，左列笔记列表（`＋ 新建笔记`、右键删除，卡片显示标题与更新时间），右侧编辑 `标题` + `正文`（多行）；输入即改内存、700ms 防抖自动加密保存，切页/切笔记/退出时立即落盘，右下角显示“编辑中… / 已自动保存 时间”；标题为空时列表显示“未命名笔记”
- 加密文件：`通用` 模块独立功能，`＋ 导入文件`（可多选、任意类型、单文件 ≤200MB）→ 读取后逐个 AES-256 加密存 `Data/files/<文件Id>.enc`，列表显示名称、大小、导入时间与合计大小；右键 `导出解密副本`（明文，另存到你选的位置，请妥善保管）、`重命名`、`删除`（删除时一并删除加密内容）
- 120 秒无操作（登录页＋主界面＋所有弹窗均计时）强制退出；右下角实时倒计时

## 4. 数据与加密

- exe 同级 `Data/` 下：`projects.dat`（项目）/ `processes.dat`（工序＋步骤文字与图片索引）/ `workload.dat`（工作量记录）/ `wprocesses.dat`（工作量工序）/ `shortcuts.dat`（通用与各项目的快捷路径）/ `mindmaps.dat`（思维导图）/ `notes.dat`（笔记）/ `files.dat`（加密文件元数据），格式均为 `IV(16字节) + AES-256-CBC 密文`
- 步骤配图单独存 `Data/images/<图片Id>.img`（每图一个加密文件），`processes.dat` 里只有元数据——这样增删步骤不会再全量重编码所有图片；加密文件内容同样单独存 `Data/files/<文件Id>.enc`
- 保存登录配置在用户文件夹 `%APPDATA%\ProjectRecorder\auto-login.dat`（DPAPI 当前 Windows 用户加密，与 `Data/` 下的 AES 数据相互独立；换 Windows 用户或删掉该文件即失效）
- 密钥 = `SHA256(密码)`；内存 JSON 序列化后加密，绝不明文落盘；不向前兼容旧版 `.dat`

## 5. 改密码 / 改超时

- 密码：不区分大小写、自动去首尾空白。改密码时对小写形式算 SHA256 hex（`python3 -c "import hashlib; print(hashlib.sha256('新密码'.lower().encode()).hexdigest())"`），替换 `src/ProjectRecorder/Services/AuthService.cs` 中 `FixedPasswordHash`，旧 `.dat` 作废；已保存的自动登录也会失效（下次启动回到登录页，可重新勾选 `保存登录`）
- 超时：改 `src/ProjectRecorder/Services/InactivityMonitor.cs` 中 `TimeoutSeconds`（当前 120）

## 6. 项目结构

```
小工具/                      本目录（编译请在这里执行）
  ProjectRecorder.sln
  publish.bat / publish.sh            一键发布（Win / Linux）
  publish/小工具.exe                  发布产物（随仓库一起发布）
  src/ProjectRecorder/
  App.xaml(.cs) / LoginWindow / MainWindow（左侧「通用模块 + 项目列表」，右侧按入口切换视图，通用含工作量/导出/快捷路径/思维导图/笔记/加密文件）
  AddProjectDialog / AddProcessDialog / StepDialog（含配图）/ PathShortcutDialog（名称+路径+浏览）/ NameInputDialog（导图命名）/ MindNodeDialog（节点标题+内容）/ MindMapHelpDialog（操作教程）/ ManualCopyDialog（手动复制网址）/ ImageViewerDialog
  Models/ProcessItem.cs(FlowStep) / WorkloadRecord.cs / WorkloadProcess.cs / PathShortcut.cs / MindMap.cs(MindNode) / NoteItem.cs(笔记) / EncryptedFile.cs(加密文件)
  Services/AuthService.cs / CryptoService.cs / DataStore.cs / InactivityMonitor.cs / ExcelExporter.cs(手写xlsx) / MindMapOutlineExporter.cs(导图文本大纲)
  Converters/ImageConverters.cs
  Panels/AdaptiveWrapPanel.cs(卡片固定尺寸、靠左、缝隙自适应填满整行)
  Panels/MindMapView.cs(思维导图画布：树形自动布局、双击改名、右键增删、拖动平移、Ctrl+滚轮缩放)
```

不进仓库的东西（本地生成，已在根目录 `.gitignore` 忽略）：`bin/`、`obj/`、`.vs/`、`publish/` 下的 config/pdb、运行时 `Data/*.dat`。`publish/小工具.exe` 是发布产物，需要随仓库一起上传。

## 7. 上传到 GitHub

```bash
git init                    # 已有仓库可跳过
git add .
git status                  # 确认没有 bin/obj/.vs/.dat 被加入；publish/小工具.exe 应该在列表中
git commit -m "init: ProjectRecorder"
git branch -M main
git remote add origin <你的仓库地址>
git push -u origin main
```
