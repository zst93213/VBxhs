using System.Windows;
using System.Windows.Controls;
using Vista.Presentation.Auth;

namespace Vista.Presentation
{
    /// <summary>
    /// 主窗口 code-behind。承载导航、账号栏、键盘事件路由、朗读触发。
    /// ViewModel 处理数据与命令；这里只处理纯 UI 事件转 ViewModel。
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm = DataContext as MainViewModel ?? ((App)Application.Current).MainWindowVm;
            DataContext = _vm;

            // 注册全局键盘快捷键（设计计划 §4.2）
            Input.KeyboardCommandCenter.Register(this, _vm);

            // 默认选中首页
            if (NavList.Items.Count > 0) NavList.SelectedIndex = 0;

            // 焦点放到信息流，方便读屏立即进入主内容区
            Keyboard.Focus(CardList);
        }

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
                _vm?.NavigateTo(tag);
        }

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            // 打开 WebView2 托管登录窗口（设计计划：WebView 托管登录决策）
            var win = new WebView2LoginWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void OnManageAccounts(object sender, RoutedEventArgs e)
        {
            // M1 实现：账号管理面板
            MessageBox.Show("账号管理面板（M1 实现）", "Vista", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnNarrateCurrent(object sender, RoutedEventArgs e)
        {
            _vm?.NarrateCurrentCard();
        }
    }
}
