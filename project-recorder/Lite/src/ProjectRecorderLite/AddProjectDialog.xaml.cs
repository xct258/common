using System.Windows;

namespace ProjectRecorderLite;

/// <summary>添加项目小弹窗：项目名称（必填）。</summary>
public partial class AddProjectDialog : Window
{
    public string ProjectName { get; private set; } = string.Empty;

    public AddProjectDialog()
    {
        InitializeComponent();
        TxtName.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("请输入项目名称。", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        ProjectName = name;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
