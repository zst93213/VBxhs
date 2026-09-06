using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vista.Core.Adapters.Models;
using Vista.Presentation.Accessibility;
using Vista.Presentation.Auth;
using Vista.Presentation.Dialogs;
using Vista.Presentation.Input;

namespace Vista.Presentation
{
    /// <summary>
    /// 主窗口 code-behind。所有事件处理在此桥接 UI ↔ ViewModel。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly KeyboardCommandCenter _kb;

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;

            // 绑定集合
            AccountChip.ItemsSource = _vm.AccountList;
            CardList.ItemsSource = _vm.Cards;
            HotCardList.ItemsSource = _vm.HotCards;
            CommentList.ItemsSource = _vm.CurrentComments;
            HotSearchList.ItemsSource = _vm.HotSearchItems;

            // 用户主页信息绑定到 DataContext
            UserProfilePanel.DataContext = _vm;

            _kb = new KeyboardCommandCenter(this, _vm);
            _kb.Attach();

            Loaded += (s, e) =>
            {
                _vm.ReloadAccounts();
                if (_vm.AccountList.Count > 0)
                    AccountChip.SelectedIndex = 0;
                Focus();
            };
        }

        public void SyncStatus(string text) => StatusText.Text = text;

        // ========== 账号 ==========

        private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountChip.SelectedItem is Vista.Accounts.AccountInfo info)
                _vm.SwitchAccount(info);
        }

        private async void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var dlg = new WebView2LoginWindow { Owner = this };
            dlg.ShowDialog();
            if (dlg.LoginSucceeded && dlg.CreatedAccount != null)
            {
                // 1) 重新加载账号列表
                _vm.ReloadAccounts();
                // 2) 自动选中刚登录的账号（ComboBox 选中项变化会触发 SwitchAccount，但保险起见再调一次）
                for (int i = 0; i < _vm.AccountList.Count; i++)
                {
                    if (_vm.AccountList[i].Uid == dlg.CreatedAccount.Uid)
                    {
                        AccountChip.SelectedIndex = i;
                        break;
                    }
                }
                _vm.SwitchAccount(dlg.CreatedAccount);
                // 3) 自动加载关注信息流（用户登录后第一眼想看的内容）
                FeedTabs.SelectedItem = TabFollowing; // 切到关注选项卡
                StatusText.Text = "登录成功，正在加载关注信息流...";
                await _vm.RefreshFeedAsync();
            }
            else
            {
                StatusText.Text = "登录未完成或已取消";
            }
        }

        private void OnManageAccounts(object sender, RoutedEventArgs e)
        {
            var dlg = new AccountManagerWindow(_vm) { Owner = this };
            dlg.ShowDialog();
        }

        // ========== 信息流 / 搜索 ==========

        /// <summary>刷新按钮：根据当前选中的选项卡刷新对应内容。</summary>
        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            if (FeedTabs?.SelectedItem is TabItem tab && tab.Tag is string tag)
            {
                switch (tag)
                {
                    case "Following": await _vm.RefreshFeedAsync(); break;
                    case "Recommend":
                        _vm.HotCards.Clear(); // 强制重新拉取
                        await _vm.LoadHotWeiboAsync(); break;
                    case "HotSearch":
                        _vm.HotSearchItems.Clear();
                        await _vm.LoadHotSearchAsync(); break;
                }
            }
            else
            {
                await _vm.RefreshFeedAsync();
            }
        }

        private async void OnHotWeibo(object sender, RoutedEventArgs e)
        {
            FeedTabs.SelectedItem = TabRecommend;
            _vm.HotCards.Clear();
            await _vm.LoadHotWeiboAsync();
        }

        private async void OnHotSearch(object sender, RoutedEventArgs e)
        {
            FeedTabs.SelectedItem = TabHotSearch;
            _vm.HotSearchItems.Clear();
            await _vm.LoadHotSearchAsync();
        }

        private async void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ShowCardView();
                await _vm.SearchAsync(SearchBox.Text);
                e.Handled = true;
            }
        }

        // ========== 互动 ==========

        private async void OnLike(object sender, RoutedEventArgs e)
            => await _vm.LikeCurrentAsync();

        private async void OnFavorite(object sender, RoutedEventArgs e)
            => await _vm.FavoriteCurrentAsync();

        private async void OnRepost(object sender, RoutedEventArgs e)
            => await _vm.RepostCurrentAsync();

        private async void OnFollowAuthor(object sender, RoutedEventArgs e)
            => await _vm.FollowCurrentAuthorAsync();

        private async void OnViewAuthor(object sender, RoutedEventArgs e)
        {
            if (_vm.CurrentCard == null)
            {
                StatusText.Text = "请先选中一张卡片";
                return;
            }
            ShowUserProfile();
            await _vm.ViewUserProfileAsync(_vm.CurrentCard.AuthorId);
        }

        private async void OnLoadComments(object sender, RoutedEventArgs e)
            => await _vm.LoadCurrentCommentsAsync();

        private async void OnSendComment(object sender, RoutedEventArgs e)
        {
            var content = CommentInput.Text;
            if (string.IsNullOrWhiteSpace(content))
            {
                StatusText.Text = "请输入评论内容";
                CommentInput.Focus();
                return;
            }
            if (_vm.CurrentCard == null)
            {
                StatusText.Text = "请先在信息流中选中一条微博";
                return;
            }
            // 防重复提交
            SendCommentBtn.IsEnabled = false;
            SendCommentBtn.Content = "发送中...";
            try
            {
                var ok = await _vm.CommentCurrentAsync(content);
                if (ok)
                {
                    CommentInput.Clear();
                    CommentInput.Focus();
                }
            }
            finally
            {
                SendCommentBtn.IsEnabled = true;
                SendCommentBtn.Content = "发送";
            }
        }

        private void OnCommentInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnSendComment(sender, e);
                e.Handled = true;
            }
        }

        private async void OnSaveOffline(object sender, RoutedEventArgs e)
            => await _vm.SaveFeedOfflineAsync();

        private async void OnCheckHealth(object sender, RoutedEventArgs e)
            => await _vm.CheckAccountHealthAsync();

        // ========== 发布 / 超话 / 设置 ==========

        private void OnPublish(object sender, RoutedEventArgs e)
        {
            var dlg = new PublishWindow(_vm) { Owner = this };
            dlg.ShowDialog();
        }

        private void OnSuperTopic(object sender, RoutedEventArgs e)
        {
            var dlg = new SuperTopicWindow(_vm) { Owner = this };
            dlg.ShowDialog();
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow { Owner = this };
            dlg.ShowDialog();
        }

        // ========== 用户主页 / 粉丝关注 ==========

        private async void OnLoadFollowers(object sender, RoutedEventArgs e)
            => await _vm.LoadFollowersAsync();

        private async void OnLoadFollowing(object sender, RoutedEventArgs e)
            => await _vm.LoadFollowingAsync();

        private async void OnFollowProfileUser(object sender, RoutedEventArgs e)
        {
            if (_vm.CurrentUserProfile != null)
                await _vm.FollowCurrentAuthorAsync();
        }

        // ========== 卡片 / 列表交互 ==========

        private void OnCardDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CardList.SelectedItem is PostCard card)
            {
                _vm.CurrentCard = card;
                _vm.LoadCurrentCommentsAsync();
            }
        }

        /// <summary>键盘选中卡片时也更新 CurrentCard，确保评论/点赞等操作有目标。
        /// 关注 Tab 和推荐 Tab 共用此处理（sender 区分）。</summary>
        private void OnCardSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListView lv && lv.SelectedItem is PostCard card)
                _vm.CurrentCard = card;
        }

        private async void OnHotSearchDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HotSearchList.SelectedItem is HotSearchItem item)
            {
                // 双击热搜 → 切到关注 Tab 并搜索该关键词
                FeedTabs.SelectedItem = TabFollowing;
                await _vm.SearchAsync(item.Keyword);
            }
        }

        // ========== 选项卡切换：自动加载对应内容 ==========

        /// <summary>切换 关注 / 推荐 / 微博热搜 选项卡时自动加载对应数据。
        /// 登录成功后默认选中"关注"也会触发此方法。</summary>
        private async void OnFeedTabChanged(object sender, SelectionChangedEventArgs e)
        {
            // 构造期间（_vm 还没赋值，或 FeedTabs 还在初始化）触发则跳过，避免 NullRef
            if (_vm == null || FeedTabs == null) return;
            if (e.RemovedItems.Count == 0 && e.AddedItems.Count > 0
                && FeedTabs.SelectedItem is TabItem tab && tab.Tag is string tag)
                _ = LoadTabContentAsync(tag);
            else if (FeedTabs.SelectedItem is TabItem t && t.Tag is string tag2)
                _ = LoadTabContentAsync(tag2);
        }

        private async System.Threading.Tasks.Task LoadTabContentAsync(string tag)
        {
            switch (tag)
            {
                case "Following":
                    if (_vm.Cards.Count == 0) await _vm.RefreshFeedAsync();
                    break;
                case "Recommend":
                    if (_vm.HotCards.Count == 0) await _vm.LoadHotWeiboAsync();
                    break;
                case "HotSearch":
                    if (_vm.HotSearchItems.Count == 0) await _vm.LoadHotSearchAsync();
                    break;
            }
        }

        // ========== 导航 ==========

        private void OnNavChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                if (tag == "Publish")
                {
                    OnPublish(sender, e);
                    return;
                }
                if (tag == "Settings")
                {
                    OnSettings(sender, e);
                    return;
                }
                // 把左侧导航映射到中栏选项卡
                switch (tag)
                {
                    case "Home": FeedTabs.SelectedItem = TabFollowing; break;
                    case "Hot": FeedTabs.SelectedItem = TabRecommend; break;
                    case "HotSearch": FeedTabs.SelectedItem = TabHotSearch; break;
                    case "Me": _vm.NavigateTo("Me"); break;
                }
            }
        }

        // ========== 视图切换辅助 ==========

        private void ShowCardView() => FeedTabs.SelectedItem = TabFollowing;
        private void ShowHotSearchView() => FeedTabs.SelectedItem = TabHotSearch;

        private void ShowUserProfile()
        {
            UserProfilePanel.Visibility = System.Windows.Visibility.Visible;
            if (_vm.CurrentUserProfile != null)
            {
                UserNameText.Text = _vm.CurrentUserProfile.Name;
                UserBioText.Text = _vm.CurrentUserProfile.Bio;
            }
        }
    }
}
