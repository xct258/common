using System;
using System.Windows;
using ProjectRecorder.Services;

namespace ProjectRecorder;

public partial class LoginWindow : Window
{
    private readonly InactivityMonitor _monitor = new();
    private int _failCount;

    public LoginWindow()
    {
        InitializeComponent();
        PwdBox.Focus();
        Loaded += LoginWindow_Loaded;
        Closed += (_, _) => _monitor.Dispose();
    }

    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
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

    private void BtnLogin_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void TryLogin()
    {
        string input = PwdBox.Password ?? string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            TxtError.Text = "请输入密码。";
            PwdBox.Focus();
            return;
        }
        if (!AuthService.Verify(input))
        {
            _failCount++;
            _monitor.NotifyActivity();
            TxtError.Text = "密码错误。";
            PwdBox.Clear();
            if (_failCount >= 2)
            {
                DialogResult = false;
                Close();
                return;
            }
            PwdBox.Focus();
            return;
        }

        try
        {
            // 预读一次：验证 .dat 可解密（首次运行文件不存在则返回空）
            // 密码不区分大小写：统一用规范形式做解密密钥
            string password = AuthService.Normalize(input);
            _ = DataStore.LoadProjects(password);
            _ = DataStore.LoadProcesses(password);
            _ = DataStore.LoadWorkload(password);
            _ = DataStore.LoadWorkloadProcesses(password);
            _ = DataStore.LoadShortcuts(password);
            AuthService.SessionPassword = password;

            // 勾选“保存登录”且密码正确：二次确认后再写入，下次自动登录
            if (ChkRemember.IsChecked == true)
            {
                var confirm = MessageBox.Show(this,
                    "确认记住密码？\n下次打开将跳过登录直接进入（仅本机当前 Windows 用户有效）。",
                    "记住密码", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                    ChkRemember.IsChecked = false;
            }

            // 勾选“保存登录”：写入用户文件夹（DPAPI 加密），下次自动登录并取消 120 秒自动退出
            if (ChkRemember.IsChecked == true)
            {
                try
                {
                    AutoLoginStore.Enable(password);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存登录失败（本次仍可正常使用）：{ex.Message}", "记住登录",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                AutoLoginStore.Disable();
            }
        }
        catch (InvalidOperationException)
        {
            TxtError.Text = "密码错误。";
            PwdBox.Clear();
            PwdBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
