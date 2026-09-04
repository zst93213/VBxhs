using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vista.Accounts;
using Vista.Presentation.Accessibility;
using Vista.Presentation.Auth;

namespace Vista.Presentation
{
    /// <summary>
    /// 主窗口 code-behind。只处理纯 UI 事件路由到 ViewModel；所有状态数据绑定到 DataContext。
    /// 争渡适配要点：
    ///   1) Loaded 时调用 ZdsCompatibility.DisableVirtualization(信息流 + 评论区)：争渡对虚拟化列表读不准。
    ///   2) Loaded 时 DemoteStatusBarLiveRegion：发现争渡进程则把状态栏 LiveSetting 降级，避免重复播报。
    ///   3) 键盘中心注册全局快捷键。
    ///   4) 状态栏 Status 与 ViewModel 绑定，TextBlock Text 变化触发 UIA LiveRegion（若为 Polite）。
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

            // --- 争渡兼容：关虚拟化 + 降 LiveRegion + 状态栏绑定 ---
            ZdsCompatibility.DisableVirtualization(CardList);
            ZdsCompatibility.DisableVirtualization(CommentList);
            ZdsCompatibility.DemoteStatusBarLiveRegion(this);

            // 状态栏文字：绑定 ViewModel.Status
            StatusText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Status") { Source = _vm });

            // 评论区绑定
            CommentList.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("CurrentComments") { Source = _vm });

            // 信息流绑定
            CardList.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Cards") { Source = _vm });

            // 账号列表绑定（ComboBox 显示 DisplayName）
            AccountChip.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("AccountList") { Source = _vm });
            AccountChip.DisplayMemberPath = "DisplayName";

            // 选中卡片 → VM.CurrentCard
            CardList.SelectionChanged += (s2, e2) =>
                _vm.CurrentCard = CardList.SelectedItem as Core.Adapters.Models.PostCard;

            // --- 全局键盘快捷键 ---
            Input.KeyboardCommandCenter.Register(this, _vm);

            // --- 默认选中：导航第 0 项，焦点放到搜索框（争渡用户 Tab 第一站） ---
            if (NavList.Items.Count > 0) NavList.SelectedIndex = 0;
            Keyboard.Focus(SearchBox);

            // 首次自动刷新（如果已登录）
            if (_vm.AccountList.Count > 0)
                _ = _vm.RefreshFeedAsync();
        }

        // ---------- 导航事件 ----------
        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
                _vm?.NavigateTo(tag);
        }

        // ---------- 账号 ----------
        private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountChip.SelectedItem is AccountInfo info)
                _vm?.SwitchAccount(info);
        }

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var win = new WebView2LoginWindow { Owner = this };
            win.ShowDialog();
            _vm?.ReloadAccounts();
            // 重新绑定账号下拉
            if (AccountChip.Items.Count > 0)
                AccountChip.SelectedIndex = AccountChip.Items.Count - 1;
        }

        private void OnManageAccounts(object sender, RoutedEventArgs e)
            => MessageBox.Show("账号管理器：添加、分组、删除已登录账号。（M1 实现）", "Vista");

        // ---------- 工具栏操作 ----------
        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RefreshFeedAsync();
        }

        private async void OnSearch(object sender, RoutedEventArgs e) => await DoSearch();

        private async System.Threading.Tasks.Task DoSearch()
        {
            if (_vm == null) return;
            var keyword = SearchBox.Text;
            await _vm.SearchAsync(keyword);
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = DoSearch();
                e.Handled = true;
            }
        }

        private async void OnLike(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.LikeCurrentAsync();
        }

        private async void OnFavorite(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.FavoriteCurrentAsync();
        }

        private async void OnRepost(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RepostOrShareCurrentAsync();
        }

        private async void OnLoadComments(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.LoadCurrentCommentsAsync();
        }

        private async void OnCheckHealth(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.CheckAccountHealthAsync();
        }

        private void OnNarrateComments(object sender, RoutedEventArgs e) => _vm?.NarrateComments();
        private void OnNarrateCurrent(object sender, RoutedEventArgs e) => _vm?.NarrateCurrentCard();

        private async void OnSaveOffline(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.SaveFeedOfflineAsync();
        }
    }
}
