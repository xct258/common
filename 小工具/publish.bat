@echo off
REM Windows 10/11 发布脚本（.NET Framework 4.8，系统自带，无需安装运行时）
REM 产物：publish\小工具.exe（约120KB），直接双击运行
REM 需要本机安装 .NET SDK（任意版本，6以上即可）用于编译
dotnet publish src/ProjectRecorder/ProjectRecorder.csproj -c Release -o publish
REM 去掉构建附带的 .config（Win10/11 自带 4.8，不需要它也能跑）
del /q publish\ProjectRecorder.exe.config 2>nul
del /q publish\ProjectRecorder.pdb 2>nul
echo.
echo 发布完成：publish\小工具.exe
echo 同级目录会生成 projects.dat / processes.dat / workload.dat / wprocesses.dat（AES-256 加密）
pause
