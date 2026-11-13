using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// MainWindow 拆分文件：窗体尺寸记忆、自动登录开关、无操作倒计时、当前班次键
/// 与 MainWindow.xaml.cs 同为一个 partial 类，可直接访问其私有字段和 x:Name 生成的控件字段。
/// </summary>
public partial class MainWindow
{
    // 记住用户调整过的窗口尺寸（存注册表 HKCU，与加密数据无关）
    private void LoadWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\ProjectRecorder");
            if (key == null) return;
            if (key.GetValue("MainW") is int w && w >= 860 && w <= 1600) Width = w;
            if (key.GetValue("MainH") is int h && h >= 580 && h <= 1000) Height = h;
        }
        catch
        {
            // 读不到就用默认尺寸
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\ProjectRecorder");
            key.SetValue("MainW", (int)ActualWidth, RegistryValueKind.DWord);
            key.SetValue("MainH", (int)ActualHeight, RegistryValueKind.DWord);
        }
        catch
        {
            // 存失败不影响退出
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PreviewMouseMove += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseDown += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseUp += (s, ev) => _monitor.NotifyActivity();
        PreviewMouseWheel += (s, ev) => _monitor.NotifyActivity();
        PreviewKeyDown += (s, ev) => _monitor.NotifyActivity();
        PreviewKeyUp += (s, ev) => _monitor.NotifyActivity();
        PreviewTextInput += (s, ev) => _monitor.NotifyActivity();

        _monitor.TimedOut += () =>
        {
            // 已记住登录：取消 120 秒自动退出（重启计时，Tick 仍用于班次刷新）
            if (_autoLogin)
            {
                _monitor.Start();
                return;
            }
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        };
        _monitor.Tick += remain =>
        {
            TxtCountdown.Text = _autoLogin
                ? "已记住登录：无操作不会自动退出"
                : $"无操作 {remain} 秒后自动退出";

            // 跨班次（如 8:30 / 21:00 / 跨日）时，工作量页面自动重新统计本班数量
            string key = ShiftService.CurrentKey();
            if (key != _shiftKey)
            {
                _shiftKey = key;
                if (ViewWorkloadTab.Visibility == Visibility.Visible)
                    ShowWorkloadTab();
            }
        };
        _shiftKey = ShiftService.CurrentKey();

        // 同步“记住登录”状态（登录页勾选过或自动登录进来时为勾选）
        _autoLogin = AutoLoginStore.IsEnabled;
        _monitor.HardExitEnabled = !_autoLogin;
        _autoLoginUiLoading = true;
        ChkAutoLogin.IsChecked = _autoLogin;
        _autoLoginUiLoading = false;
        UpdateCountdownText();
        _monitor.Start();

        RefreshProjects();
    }

    // 状态栏“记住登录”开关：勾选保存登录信息并取消自动退出；取消勾选删除配置并恢复计时
    private void ChkAutoLogin_Changed(object sender, RoutedEventArgs e)
    {
        if (_autoLoginUiLoading) return;
        _monitor.NotifyActivity();

        if (ChkAutoLogin.IsChecked == true)
        {
            // 二次确认：避免误勾选后自动登录、取消 120 秒自动退出
            var confirm = MessageBox.Show(
                "确定要记住登录吗？\n勾选后下次启动将自动登录，并且不再进行 120 秒无操作自动退出。",
                "记住登录", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                SetAutoLoginChecked(false);
                return;
            }

            string? password = AuthService.SessionPassword;
            if (string.IsNullOrEmpty(password))
            {
                SetAutoLoginChecked(false);
                MessageBox.Show("无法获取当前登录信息，请重新登录后再试。", "记住登录",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                AutoLoginStore.Enable(password!);
                _autoLogin = true;
                _monitor.HardExitEnabled = false;
            }
            catch (Exception ex)
            {
                SetAutoLoginChecked(false);
                MessageBox.Show($"保存登录信息失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            AutoLoginStore.Disable();
            _autoLogin = false;
            _monitor.HardExitEnabled = true;
        }

        _monitor.Start();
        UpdateCountdownText();
    }

    private void SetAutoLoginChecked(bool value)
    {
        _autoLoginUiLoading = true;
        ChkAutoLogin.IsChecked = value;
        _autoLoginUiLoading = false;
    }

    private void UpdateCountdownText()
    {
        TxtCountdown.Text = _autoLogin
            ? "已记住登录：无操作不会自动退出"
            : $"无操作 {InactivityMonitor.TimeoutSeconds} 秒后自动退出";
    }
}
