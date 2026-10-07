using System;
using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

/// <summary>
/// 首次运行设置密码：写入 Data/auth.dat 校验值，之后启动走登录。
/// </summary>
public partial class SetPasswordWindow : Window
{
    private readonly InactivityMonitor _monitor = new();

    public SetPasswordWindow()
    {
        InitializeComponent();
        PwdBox.Focus();
        Loaded += SetPasswordWindow_Loaded;
        Closed += (_, _) => _monitor.Dispose();
    }

    private void SetPasswordWindow_Loaded(object sender, RoutedEventArgs e)
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
            DialogResult = false;
            Close();
        };
        _monitor.Start();
    }

    private void BtnSet_Click(object sender, RoutedEventArgs e)
    {
        string pwd = PwdBox.Password ?? string.Empty;
        string confirm = PwdConfirm.Password ?? string.Empty;

        // 与登录一致：去首尾空白 + 不区分大小写
        string norm = AuthService.Normalize(pwd);
        string normConfirm = AuthService.Normalize(confirm);

        if (norm.Length == 0)
        {
            TxtError.Text = "请输入密码。";
            PwdBox.Focus();
            return;
        }
        if (norm.Length < 4)
        {
            TxtError.Text = "密码至少 4 位。";
            PwdBox.Focus();
            return;
        }
        if (!string.Equals(norm, normConfirm, StringComparison.Ordinal))
        {
            TxtError.Text = "两次输入的密码不一致。";
            PwdConfirm.Clear();
            PwdConfirm.Focus();
            return;
        }

        try
        {
            AuthService.SetPassword(norm);
            AuthService.SessionPassword = norm;
        }
        catch (Exception ex)
        {
            TxtError.Text = $"设置失败：{ex.Message}";
            return;
        }

        DialogResult = true;
        Close();
    }
}
