using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;
using Vista.Features.Weibo.UseCases;
using Vista.Infrastructure.Cache;

namespace Vista.Presentation
{
    /// <summary>
    /// 主窗口 ViewModel（纯微博版）。
    /// 所有功能均落地：信息流、搜索、热门、热搜、评论、点赞、收藏、转发、发布、
    /// 用户主页、粉丝/关注、超话浏览与签到、账号管理、设置。
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly GetHomeTimelineUseCase _weiboFeed;
        private readonly SearchUseCase _weiboSearch;
        private readonly RepostUseCase _weiboRepost;
        private readonly AccountContext _accountContext;
        private readonly AccountRepository _accounts;
        private readonly ICacheStore _cache;

        public ObservableCollection<PostCard> Cards { get; } = new ObservableCollection<PostCard>();
        public ObservableCollection<AccountInfo> AccountList { get; } = new ObservableCollection<AccountInfo>();
        public ObservableCollection<Comment> CurrentComments { get; } = new ObservableCollection<Comment>();
        public ObservableCollection<HotSearchItem> HotSearchItems { get; } = new ObservableCollection<HotSearchItem>();
        public ObservableCollection<UserProfile> UserRelations { get; } = new ObservableCollection<UserProfile>();

        [ObservableProperty] private string _status = "就绪";
        [ObservableProperty] private PostCard _currentCard;
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private bool _offlineMode;
        [ObservableProperty] private UserProfile _currentUserProfile;
        [ObservableProperty] private string _currentView = "Home";

        public MainViewModel(
            GetHomeTimelineUseCase weiboFeed,
            SearchUseCase weiboSearch,
            RepostUseCase weiboRepost,
            AccountContext accountContext,
            AccountRepository accounts,
            ICacheStore cache)
        {
            _weiboFeed = weiboFeed;
            _weiboSearch = weiboSearch;
            _weiboRepost = weiboRepost;
            _accountContext = accountContext;
            _accounts = accounts;
            _cache = cache;
        }

        public void SetAdapter(IPlatformAdapter weibo) => WeiboAdapterRef = weibo;
        internal IPlatformAdapter WeiboAdapterRef { get; set; }
        private IPlatformAdapter CurrentAdapter => WeiboAdapterRef;

        // ========== 账号 ==========

        public void ReloadAccounts()
        {
            AccountList.Clear();
            foreach (var a in _accounts.List())
                AccountList.Add(a);
            if (AccountList.Count > 0 && _accountContext.Current == null)
                _accountContext.SwitchTo(AccountList[0].ToAccountId());
            Status = AccountList.Count > 0
                ? $"已加载 {AccountList.Count} 个账号"
                : "尚未登录任何账号，请先添加账号";
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        public void SwitchAccount(AccountInfo info)
        {
            if (info == null) return;
            _accountContext.SwitchTo(info.ToAccountId());
            Status = $"已切换到：{info.DisplayName}";
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        /// <summary>删除账号（移除凭证与元数据）。</summary>
        public bool DeleteAccount(AccountInfo info)
        {
            if (info == null) return false;
            var id = info.ToAccountId();
            var removed = _accounts.Revoke(id);
            if (removed)
            {
                ReloadAccounts();
                if (_accountContext.Current == id)
                    _accountContext.Clear();
                Status = $"已删除账号：{info.DisplayName}";
            }
            else Status = "删除账号失败";
            Accessibility.NarrationService.SpeakAuto(Status);
            return removed;
        }

        // ========== 导航（真正切换内容视图） ==========

        public async void NavigateTo(string tag)
        {
            CurrentView = tag;
            switch (tag)
            {
                case "Home":
                    Status = "首页";
                    await RefreshFeedAsync();
                    break;
                case "Hot":
                    Status = "热门微博";
                    await LoadHotWeiboAsync();
                    break;
                case "HotSearch":
                    Status = "热搜榜";
                    await LoadHotSearchAsync();
                    break;
                case "Publish":
                    Status = "发布微博";
                    break;
                case "Me":
                    Status = "我的主页";
                    if (_accountContext.Current != null)
                        await ViewUserProfileAsync(_accountContext.Current.Value.Uid);
                    break;
                default:
                    Status = "已切换到：" + tag;
                    break;
            }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        // ========== 朗读（手动） ==========

        public void NarrateCurrentCard()
        {
            if (CurrentCard == null) { Status = "当前无卡片可朗读"; return; }
            Accessibility.NarrationService.SpeakManual(CurrentCard.SpokenLabel);
            Status = "正在朗读：" + CurrentCard.AuthorName;
        }

        public void NarrateComments()
        {
            if (CurrentComments.Count == 0)
            {
                Status = "当前评论区为空，或尚未加载";
                Accessibility.NarrationService.SpeakManual("当前没有评论");
                return;
            }
            Accessibility.NarrationService.SpeakCommentsManual(CurrentComments);
            Status = $"正在朗读 {CurrentComments.Count} 条评论";
        }

        // ========== 信息流 / 搜索 / 热门 / 热搜 ==========

        public async Task RefreshFeedAsync()
        {
            if (_accountContext.Current == null) { Status = "请先添加并选择账号"; return; }
            IsRefreshing = true;
            Status = "正在刷新信息流...";
            try
            {
                var result = await _weiboFeed.ExecuteAsync(null, default);
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"已加载 {Cards.Count} 条微博";
                OfflineMode = false;
                await SaveFeedOfflineAsync(silent: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "刷新信息流失败");
                Status = $"刷新失败：{ex.Message}，尝试从离线缓存恢复";
                var cached = await TryLoadOfflineAsync();
                OfflineMode = cached;
            }
            finally { IsRefreshing = false; }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        public async Task SearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) { Status = "请输入搜索关键词"; return; }
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            IsRefreshing = true;
            Status = $"正在搜索：{keyword}";
            try
            {
                var result = await _weiboSearch.ExecuteAsync(keyword, "popular", null, default);
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"已找到 {Cards.Count} 条结果";
                OfflineMode = false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "搜索失败");
                Status = "搜索失败：" + ex.Message;
            }
            finally { IsRefreshing = false; }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        /// <summary>热门微博推荐流。</summary>
        public async Task LoadHotWeiboAsync()
        {
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            IsRefreshing = true;
            Status = "正在加载热门微博...";
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return; }
                var result = await adapter.GetHotWeiboAsync(_accountContext.EnsureCurrent(), null, default);
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"已加载 {Cards.Count} 条热门微博";
                OfflineMode = false;
            }
            catch (Exception ex) { Status = "加载热门微博失败：" + ex.Message; }
            finally { IsRefreshing = false; }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        /// <summary>热搜榜。</summary>
        public async Task LoadHotSearchAsync()
        {
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            IsRefreshing = true;
            Status = "正在加载热搜榜...";
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return; }
                var list = await adapter.GetHotSearchAsync(_accountContext.EnsureCurrent(), default);
                HotSearchItems.Clear();
                foreach (var item in list) HotSearchItems.Add(item);
                Status = $"已加载 {HotSearchItems.Count} 条热搜";
            }
            catch (Exception ex) { Status = "加载热搜失败：" + ex.Message; }
            finally { IsRefreshing = false; }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        /// <summary>点击某条热搜 → 以该关键词搜索。</summary>
        public async void SearchHotItem(HotSearchItem item)
        {
            if (item == null) return;
            await SearchAsync(item.Keyword);
        }

        private async Task<bool> TryLoadOfflineAsync()
        {
            try
            {
                var offline = await _cache.GetAsync<PagedResult<PostCard>>("offline:home");
                if (offline?.Items == null || offline.Items.Count == 0) return false;
                Cards.Clear();
                foreach (var card in offline.Items) Cards.Add(card);
                Status = $"离线模式：已加载 {Cards.Count} 条缓存内容";
                return true;
            }
            catch { return false; }
        }

        public async Task SaveFeedOfflineAsync(bool silent = false)
        {
            if (Cards.Count == 0) { if (!silent) Status = "没有可缓存的内容"; return; }
            try
            {
                var result = new PagedResult<PostCard>(Cards.ToList(), null);
                await _cache.SetAsync("offline:home", result, TimeSpan.FromDays(7));
                Status = $"已缓存 {Cards.Count} 条内容，离线可用 7 天";
                if (!silent)
                    Accessibility.NarrationService.SpeakAuto($"已缓存 {Cards.Count} 条内容");
            }
            catch (Exception ex) { Status = "缓存失败：" + ex.Message; }
        }

        // ========== 详情 / 评论 ==========

        public async Task<bool> LoadCurrentCommentsAsync()
        {
            if (CurrentCard == null) { Status = "请先选中一张卡片"; return false; }
            try
            {
                Status = "正在加载评论...";
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                var page = await adapter.GetCommentsAsync(_accountContext.EnsureCurrent(), CurrentCard.Id, null, default);
                CurrentComments.Clear();
                foreach (var c in page.Items.Take(50)) CurrentComments.Add(c);
                Status = $"已加载 {CurrentComments.Count} 条评论";
                Accessibility.NarrationService.SpeakAuto(Status);
                return true;
            }
            catch (Exception ex) { Status = "加载评论失败：" + ex.Message; return false; }
        }

        // ========== 互动 ==========

        public async Task<bool> RepostCurrentAsync(string comment = "")
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            try
            {
                Status = "正在转发微博：" + CurrentCard.AuthorName;
                var ok = await _weiboRepost.ExecuteAsync(CurrentCard.Id, comment);
                Status = ok ? "转发成功" : "转发失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex) { Status = "操作失败：" + ex.Message; return false; }
        }

        public async Task<bool> LikeCurrentAsync()
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在点赞：" + CurrentCard.AuthorName;
                var ok = await adapter.LikeAsync(_accountContext.EnsureCurrent(), CurrentCard.Id, default);
                Status = ok ? "已点赞" : "点赞失败";
                if (ok) CurrentCard.LikeCount++;
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex) { Status = "点赞失败：" + ex.Message; return false; }
        }

        public async Task<bool> FavoriteCurrentAsync()
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在收藏：" + CurrentCard.AuthorName;
                var ok = await adapter.FavoriteAsync(_accountContext.EnsureCurrent(), CurrentCard.Id, default);
                Status = ok ? "已收藏" : "收藏失败";
                if (ok) CurrentCard.CollectCount++;
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex) { Status = "收藏失败：" + ex.Message; return false; }
        }

        public async Task<bool> CommentCurrentAsync(string content)
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            if (string.IsNullOrWhiteSpace(content)) { Status = "评论内容为空"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在发表评论";
                var c = await adapter.CommentAsync(_accountContext.EnsureCurrent(), CurrentCard.Id, content, null, default);
                if (c != null)
                {
                    CurrentComments.Insert(0, c);
                    CurrentCard.CommentCount++;
                    Status = "评论成功";
                }
                else Status = "评论失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return c != null;
            }
            catch (Exception ex) { Status = "评论失败：" + ex.Message; return false; }
        }

        /// <summary>发布微博（纯文本或带图片路径）。</summary>
        public async Task<bool> PublishPostAsync(string text, string[] imagePaths = null)
        {
            if (_accountContext.Current == null) { Status = "请先选择账号"; return false; }
            if (string.IsNullOrWhiteSpace(text) && (imagePaths == null || imagePaths.Length == 0))
            { Status = "请输入微博内容或选择图片"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在发布微博...";
                var req = new PublishRequest
                {
                    Text = text ?? "",
                    ImagePaths = imagePaths
                };
                var id = await adapter.PublishAsync(_accountContext.EnsureCurrent(), req, default);
                Status = !string.IsNullOrEmpty(id) ? "发布成功" : "发布失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return !string.IsNullOrEmpty(id);
            }
            catch (Exception ex) { Status = "发布失败：" + ex.Message; return false; }
        }

        /// <summary>关注当前卡片作者。</summary>
        public async Task<bool> FollowCurrentAuthorAsync()
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            if (string.IsNullOrEmpty(CurrentCard.AuthorId)) { Status = "无法获取作者 ID"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在关注：" + CurrentCard.AuthorName;
                var ok = await adapter.FollowAsync(_accountContext.EnsureCurrent(), CurrentCard.AuthorId, default);
                Status = ok ? $"已关注 {CurrentCard.AuthorName}" : "关注失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex) { Status = "关注失败：" + ex.Message; return false; }
        }

        /// <summary>取消关注指定用户。</summary>
        public async Task<bool> UnfollowUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                var ok = await adapter.UnfollowAsync(_accountContext.EnsureCurrent(), userId, default);
                Status = ok ? "已取消关注" : "取消关注失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex) { Status = "取消关注失败：" + ex.Message; return false; }
        }

        // ========== 用户主页 ==========

        /// <summary>查看指定用户主页（资料 + 其微博）。</summary>
        public async Task<bool> ViewUserProfileAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) { Status = "用户 ID 为空"; return false; }
            if (_accountContext.Current == null) { Status = "请先选择账号"; return false; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return false; }
                Status = "正在加载用户主页...";
                CurrentUserProfile = await adapter.GetUserProfileAsync(_accountContext.EnsureCurrent(), userId, default);
                if (CurrentUserProfile != null)
                {
                    var posts = await adapter.GetUserPostsAsync(_accountContext.EnsureCurrent(), userId, null, default);
                    Cards.Clear();
                    foreach (var card in posts.Items) Cards.Add(card);
                    Status = $"{CurrentUserProfile.Name}：{CurrentUserProfile.PostCount} 条微博，{CurrentUserProfile.FollowerCount} 粉丝";
                }
                else Status = "加载用户主页失败";
                Accessibility.NarrationService.SpeakAuto(Status);
                return CurrentUserProfile != null;
            }
            catch (Exception ex) { Status = "加载用户主页失败：" + ex.Message; return false; }
        }

        /// <summary>加载当前查看用户的粉丝列表。</summary>
        public async Task LoadFollowersAsync()
        {
            if (CurrentUserProfile == null) { Status = "请先查看用户主页"; return; }
            await LoadUserRelationsAsync(CurrentUserProfile.Id, isFollowers: true);
        }

        /// <summary>加载当前查看用户的关注列表。</summary>
        public async Task LoadFollowingAsync()
        {
            if (CurrentUserProfile == null) { Status = "请先查看用户主页"; return; }
            await LoadUserRelationsAsync(CurrentUserProfile.Id, isFollowers: false);
        }

        private async Task LoadUserRelationsAsync(string userId, bool isFollowers)
        {
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return; }
                Status = "正在加载" + (isFollowers ? "粉丝" : "关注") + "列表...";
                var page = isFollowers
                    ? await adapter.GetUserFollowersAsync(_accountContext.EnsureCurrent(), userId, null, default)
                    : await adapter.GetUserFollowingAsync(_accountContext.EnsureCurrent(), userId, null, default);
                UserRelations.Clear();
                foreach (var u in page.Items) UserRelations.Add(u);
                Status = $"已加载 {UserRelations.Count} 位" + (isFollowers ? "粉丝" : "关注");
                Accessibility.NarrationService.SpeakAuto(Status);
            }
            catch (Exception ex) { Status = "加载列表失败：" + ex.Message; }
        }

        // ========== 超话 ==========

        /// <summary>加载指定超话的信息流。</summary>
        public async Task LoadSuperTopicFeedAsync(string superTopicId)
        {
            if (string.IsNullOrEmpty(superTopicId)) { Status = "请输入超话 ID"; return; }
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return; }
                Status = "正在加载超话：" + superTopicId;
                var result = await adapter.GetSuperTopicFeedAsync(_accountContext.EnsureCurrent(), superTopicId, null, default);
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"超话已加载 {Cards.Count} 条帖子";
                Accessibility.NarrationService.SpeakAuto(Status);
            }
            catch (Exception ex) { Status = "加载超话失败：" + ex.Message; }
        }

        /// <summary>超话签到。</summary>
        public async Task SignInSuperTopicAsync(string superTopicId)
        {
            if (string.IsNullOrEmpty(superTopicId)) { Status = "请输入超话 ID"; return; }
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) { Status = "无可用 Adapter"; return; }
                Status = "正在签到超话：" + superTopicId;
                var r = await adapter.SignInSuperTopicAsync(_accountContext.EnsureCurrent(), superTopicId, default);
                Status = r.Success
                    ? $"签到成功，已连续 {r.ContinuousDays} 天"
                    : "签到失败：" + r.Message;
                Accessibility.NarrationService.SpeakAuto(Status);
            }
            catch (Exception ex) { Status = "签到失败：" + ex.Message; }
        }

        // ========== 账号健康度 ==========

        public async Task CheckAccountHealthAsync()
        {
            if (_accountContext.Current == null) { Status = "请先选择账号"; return; }
            try
            {
                var adapter = CurrentAdapter;
                if (adapter == null) return;
                Status = "正在检查账号状态...";
                var h = await adapter.CheckAccountHealthAsync(_accountContext.EnsureCurrent(), default);
                Status = h.SpokenSummary;
                Accessibility.NarrationService.SpeakAuto(Status);
            }
            catch (Exception ex) { Status = "状态检查失败：" + ex.Message; }
        }
    }
}
