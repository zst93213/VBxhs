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

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var dlg = new WebView2LoginWindow { Owner = this };
            var result = dlg.ShowDialog();
            if (result == true || dlg.LoginSucceeded)
            {
                _vm.ReloadAccounts();
                StatusText.Text = "登录成功，已保存账号";
            }
        }

        private void OnManageAccounts(object sender, RoutedEventArgs e)
        {
            var dlg = new AccountManagerWindow(_vm) { Owner = this };
            dlg.ShowDialog();
        }

        // ========== 信息流 / 搜索 ==========

        private async void OnRefresh(object sender, RoutedEventArgs e)
            => await _vm.RefreshFeedAsync();

        private async void OnHotWeibo(object sender, RoutedEventArgs e)
        {
            ShowCardView();
            await _vm.LoadHotWeiboAsync();
        }

        private async void OnHotSearch(object sender, RoutedEventArgs e)
        {
            ShowHotSearchView();
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

        /// <summary>键盘选中卡片时也更新 CurrentCard，确保评论/点赞等操作有目标。</summary>
        private void OnCardSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CardList.SelectedItem is PostCard card)
                _vm.CurrentCard = card;
        }

        private async void OnHotSearchDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HotSearchList.SelectedItem is HotSearchItem item)
            {
                ShowCardView();
                await _vm.SearchAsync(item.Keyword);
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
                _vm.NavigateTo(tag);
                if (tag == "HotSearch")
                    ShowHotSearchView();
                else
                    ShowCardView();
            }
        }

        // ========== 视图切换辅助 ==========

        private void ShowCardView()
        {
            CardList.Visibility = System.Windows.Visibility.Visible;
            HotSearchList.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void ShowHotSearchView()
        {
            CardList.Visibility = System.Windows.Visibility.Collapsed;
            HotSearchList.Visibility = System.Windows.Visibility.Visible;
        }

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
