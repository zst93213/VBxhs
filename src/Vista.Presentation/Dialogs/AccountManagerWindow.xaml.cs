using System.Windows;
using Vista.Accounts;

namespace Vista.Presentation.Dialogs
{
    /// <summary>账号管理对话框：列出所有账号，支持删除。</summary>
    public partial class AccountManagerWindow : Window
    {
        private readonly MainViewModel _vm;

        public AccountManagerWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            AccountListBox.ItemsSource = _vm.AccountList;
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (AccountListBox.SelectedItem is AccountInfo info)
            {
                var result = MessageBox.Show(
                    $"确定要删除账号 {info.DisplayName} 吗？删除后需重新扫码登录。",
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    _vm.DeleteAccount(info);
            }
            else
            {
                MessageBox.Show("请先选择要删除的账号", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
