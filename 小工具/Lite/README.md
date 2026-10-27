# 小工具 Lite（ProjectRecorderLite）

从完整版 `小工具` 精简而来：**去掉登录、密码、加密、无操作自动退出，以及工作量 / 工序 / 笔记 / 思维导图 / 加密文件 / Excel 导出等功能**，只保留：

- **通用模块**：固定在项目列表第一项，可添加文件夹和网址（**网址只能放在通用**）
- **添加项目**：新增、上移 / 下移、删除（连同其下快捷路径一起删除）
- **快捷路径**：
  - 通用：文件夹 + 网址
  - 项目：仅文件夹
  - 文件夹：浏览选择，单击用资源管理器打开
  - 网址：自动补 `https://`，单击打开手动复制窗口（不会自动写入剪贴板）

数据以**明文 JSON** 直接追加在 **exe 末尾**，单文件；不再加密、无需密码。保存后正常退出程序，数据会写回 exe，复制 exe 即连数据一起复制。

## 1. 运行环境

- Windows 10 / 11（自带 .NET Framework 4.8，无需安装运行时）
- 产物：`publish/小工具Lite.exe`（单文件，直接双击）

## 2. 编译发布

推荐在 **Windows** 上编译：

1. 安装 .NET SDK（8.x 或 10.x，仅编译用）：
   https://dotnet.microsoft.com/download
2. 验证：`dotnet --list-sdks`
3. 在 `Lite/` 目录双击 `publish.bat`，或在命令行执行：

   ```
   dotnet publish src/ProjectRecorderLite/ProjectRecorderLite.csproj -c Release -o publish
   ```

Linux / macOS（仅编译，exe 仍需到 Windows 运行）：

```bash
chmod +x publish.sh && ./publish.sh
```

## 3. 使用

1. 双击 `小工具Lite.exe`，左侧列表第一项是固定的「通用」模块，其后是项目。
2. 选中「通用」后，右侧可添加文件夹和网址：
   - **文件夹**：点「浏览…」选目录（也可手输），确定后单击卡片即可打开。
   - **网址**：输入网址（可不写协议，自动补 `https://`），确定后单击卡片打开手动复制窗口，全选后按 `Ctrl+C` 复制。
3. 选中普通项目后，右侧只能添加文件夹（**网址只能在「通用」中添加**）。
4. 文件夹 / 网址卡片右键可删除（网址可右键「手动复制网址」）。
5. 左侧项目右键可 `上移` / `下移` / `删除该项目`；「通用」是固定模块，不能上移/删除。
6. 数据保存在 exe 内；改完请**正常关闭程序**，退出后才会写回 exe。

## 4. 数据（单文件）

Lite 版不生成 `Data` 文件夹，数据以明文 JSON 直接追加在 `小工具Lite.exe` 末尾。

- 每次增删改都会保存：先生成 `小工具Lite.exe.pending`，程序退出后由后台脚本覆盖回 exe。
- 正常退出后目录里只剩 `小工具Lite.exe`；异常结束时会留下 `.pending`，下次启动会自动读取并重试写回。
- 复制 / 发给别人时，复制 exe 即可，数据就在里面。
- 请先把 exe 解压 / 复制到固定文件夹再运行；直接在压缩包里双击时，临时目录可能被清理，数据会丢。
- **升级 / 替换 exe 会覆盖里面的数据；升级前先备份旧 exe。**
- 旧版 `Data/data.json` 会自动读取一次；首次保存后数据进入 exe，旧 `Data` 文件夹可自行删除。
- 数据损坏时会在 exe 同级生成 `.corrupt-<时间>.json` 备份，并以空数据启动。
- 运行中的 exe 被系统锁定，**保存后请正常退出程序**再复制 exe。
- 杀毒软件可能对「自修改 exe」报警；若保存失败，请把 exe 放到有写权限的目录。
- 每次保存后 exe 的哈希会变化，SmartScreen / 杀软可能重新提示，这是单文件存数据的正常现象。
- 窗口尺寸记忆在注册表 `HKCU\Software\ProjectRecorderLite`（与数据无关）。
- 错误日志写到 `%TEMP%\小工具Lite-error.log`，不在 exe 旁边留文件。

## 5. 目录结构

```
Lite/
  ProjectRecorderLite.sln
  publish.bat / publish.sh
  src/ProjectRecorderLite/
    App.xaml(.cs)                  启动、单实例、全局异常日志
    MainWindow.xaml(.cs)           左：通用 + 项目列表   右：快捷路径
    AddProjectDialog.xaml(.cs)     添加项目
    PathShortcutDialog.xaml(.cs)   添加快捷路径（通用可选文件夹/网址，项目仅文件夹）
    ManualCopyDialog.xaml(.cs)     手动复制窗口（已全选，按 Ctrl+C 复制）
    Models/PathShortcut.cs         快捷路径模型
    Services/ExeDataStore.cs       明文 JSON 追加进 exe / pending 写回
    Services/LiteStore.cs          项目 + 快捷路径业务存储
    Views/ShortcutView.xaml(.cs)   快捷路径面板
    Helpers/VisualTreeHelpers.cs   视觉树辅助
```

## 6. 与完整版的差异

| 功能 | 完整版 | Lite 版 |
|---|---|---|
| 登录 / 密码 | 有 | 无 |
| AES 加密存储 | 有 | 无（明文 JSON） |
| 数据位置 | Data/data.json | exe 末尾（单文件） |
| 无操作自动退出 | 有（120 秒） | 无 |
| 添加项目 | 有 | 有 |
| 快捷路径 | 有（通用模块含网址，项目仅文件夹） | 有（通用含文件夹 + 网址，项目仅文件夹） |
| 工序 / 工作量 / 笔记 / 思维导图 / 加密文件 / Excel 导出 | 有 | 无 |
