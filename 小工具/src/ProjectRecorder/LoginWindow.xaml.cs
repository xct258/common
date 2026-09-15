using System;
using System.Windows;
using System.Windows.Input;
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

    private void PwdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryLogin();
    }

    private void TryLogin()
    {
        string input = PwdBox.Password ?? string.Empty;
        if (!AuthService.Verify(input))
        {
            _failCount++;
            _monitor.NotifyActivity();
            if (_failCount >= 2)
            {
                MessageBox.Show("口令错误次数过多，程序自动退出。", "登录",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = false;
                Close();
                return;
            }
            TxtError.Text = "口令错误，拒绝访问（还剩 1 次机会）。";
            PwdBox.Clear();
            PwdBox.Focus();
            return;
        }

        try
        {
            // 预读一次：验证 .dat 可解密（首次运行文件不存在则返回空）
            _ = DataStore.LoadProjects(input);
            _ = DataStore.LoadProcesses(input);
            _ = DataStore.LoadWorkload(input);
            _ = DataStore.LoadWorkloadProcesses(input);
        }
        catch (InvalidOperationException ex)
        {
            TxtError.Text = ex.Message;
            PwdBox.Clear();
            PwdBox.Focus();
            return;
        }

        AuthService.SessionPassword = input;
        DialogResult = true;
        Close();
    }
}
