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
using Vista.Features.Xiaohongshu.UseCases;
using Vista.Infrastructure.Cache;

namespace Vista.Presentation
{
    /// <summary>
    /// 主窗口 ViewModel。
    /// 争渡兼容要点：
    ///  - 自动通知（"转发成功"等）走 UIA LiveRegion + 状态栏文字，不主动 SpeakAuto（默认关闭）。
    ///  - 手动朗读（按钮/快捷键）用 SpeakManual，始终允许（用户明确选择）。
    /// 双平台用例路由：根据 AccountContext.Current.Platform 选择对应平台的用例实例。
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly GetHomeTimelineUseCase _weiboFeed;
        private readonly SearchNotesUseCase _xhsSearch;
        private readonly RepostUseCase _weiboRepost;
        private readonly ShareNoteUseCase _xhsShare;
        private readonly GetCommentsUseCase _getComments;
        private readonly AccountContext _accountContext;
        private readonly AccountRepository _accounts;
        private readonly ICacheStore _cache;

        public ObservableCollection<PostCard> Cards { get; } = new ObservableCollection<PostCard>();
        public ObservableCollection<AccountInfo> AccountList { get; } = new ObservableCollection<AccountInfo>();
        public ObservableCollection<Comment> CurrentComments { get; } = new ObservableCollection<Comment>();

        [ObservableProperty] private string _status = "就绪";
        [ObservableProperty] private PostCard _currentCard;
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private bool _offlineMode;

        public MainViewModel(
            GetHomeTimelineUseCase weiboFeed,
            SearchNotesUseCase xhsSearch,
            RepostUseCase weiboRepost,
            ShareNoteUseCase xhsShare,
            GetCommentsUseCase getComments,
            AccountContext accountContext,
            AccountRepository accounts,
            ICacheStore cache)
        {
            _weiboFeed = weiboFeed;
            _xhsSearch = xhsSearch;
            _weiboRepost = weiboRepost;
            _xhsShare = xhsShare;
            _getComments = getComments;
            _accountContext = accountContext;
            _accounts = accounts;
            _cache = cache;
        }

        /// <summary>Adapter 注入点（由 App 在装配时设置，便于通用交互用例）。</summary>
        public void SetAdapters(IPlatformAdapter weibo, IPlatformAdapter xhs)
        {
            WeiboAdapterRef = weibo;
            XhsAdapterRef = xhs;
        }

        // 由 App 直接赋值
        internal IPlatformAdapter WeiboAdapterRef { get; set; }
        internal IPlatformAdapter XhsAdapterRef { get; set; }

        /// <summary>当前平台对应的 Adapter。</summary>
        private IPlatformAdapter CurrentAdapter
        {
            get
            {
                var p = _accountContext.Current?.Platform ?? PlatformId.Weibo;
                return p == PlatformId.Xiaohongshu ? XhsAdapterRef : WeiboAdapterRef;
            }
        }

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

        /// <summary>切换到指定账号。</summary>
        public void SwitchAccount(AccountInfo info)
        {
            if (info == null) return;
            _accountContext.SwitchTo(info.ToAccountId());
            Status = $"已切换到：{info.DisplayName}";
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        // ========== 导航 ==========

        public void NavigateTo(string tag)
        {
            Status = "已切换到：" + tag;
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        // ========== 朗读（手动） ==========

        public void NarrateCurrentCard()
        {
            if (CurrentCard == null)
            {
                Status = "当前无卡片可朗读";
                return;
            }
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

        // ========== 信息流 / 搜索 / 离线缓存 ==========

        public async Task RefreshFeedAsync()
        {
            if (_accountContext.Current == null)
            {
                Status = "请先添加并选择账号";
                return;
            }
            IsRefreshing = true;
            Status = "正在刷新信息流...";
            try
            {
                PagedResult<PostCard> result;
                var platform = _accountContext.Current.Value.Platform;
                if (platform == PlatformId.Xiaohongshu && XhsAdapterRef != null)
                {
                    // 小红书首页用 homefeed
                    result = await XhsAdapterRef.GetHomeTimelineAsync(_accountContext.EnsureCurrent(), null, default);
                }
                else
                {
                    result = await _weiboFeed.ExecuteAsync(null, default);
                }
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"已加载 {Cards.Count} 条内容";
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
            finally
            {
                IsRefreshing = false;
            }
            Accessibility.NarrationService.SpeakAuto(Status);
        }

        public async Task SearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                Status = "请输入搜索关键词";
                return;
            }
            if (_accountContext.Current == null)
            {
                Status = "请先选择账号";
                return;
            }
            IsRefreshing = true;
            Status = $"正在搜索：{keyword}";
            try
            {
                PagedResult<PostCard> result;
                var platform = _accountContext.Current.Value.Platform;
                if (platform == PlatformId.Xiaohongshu)
                    result = await _xhsSearch.ExecuteAsync(keyword, "popular", null, default);
                else
                    result = await WeiboAdapterRef.SearchPostsAsync(_accountContext.EnsureCurrent(), keyword, "popular", null, default);
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
            catch (Exception ex)
            {
                Status = "缓存失败：" + ex.Message;
            }
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
                foreach (var c in page.Items.Take(50))
                    CurrentComments.Add(c);
                Status = $"已加载 {CurrentComments.Count} 条评论";
                Accessibility.NarrationService.SpeakAuto(Status);
                return true;
            }
            catch (Exception ex)
            {
                Status = "加载评论失败：" + ex.Message;
                return false;
            }
        }

        // ========== 互动（双平台路由） ==========

        /// <summary>一键转发（微博） / 一键分享（小红书）：根据当前账号平台自动切换。</summary>
        public async Task<bool> RepostOrShareCurrentAsync(string comment = "")
        {
            if (CurrentCard == null) { Status = "当前无卡片可操作"; return false; }
            var platform = _accountContext.Current?.Platform;
            try
            {
                bool ok;
                if (platform == PlatformId.Weibo)
                {
                    Status = "正在转发微博：" + CurrentCard.AuthorName;
                    ok = await _weiboRepost.ExecuteAsync(CurrentCard.Id, comment);
                    Status = ok ? "转发成功" : "转发失败";
                }
                else if (platform == PlatformId.Xiaohongshu)
                {
                    Status = "正在分享笔记：" + CurrentCard.AuthorName;
                    ok = await _xhsShare.ExecuteAsync(CurrentCard.Id, comment);
                    Status = ok ? "分享链接已复制到剪贴板" : "分享失败";
                }
                else
                {
                    Status = "请先选择一个账号"; return false;
                }
                Accessibility.NarrationService.SpeakAuto(Status);
                return ok;
            }
            catch (Exception ex)
            {
                Status = "操作失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>一键点赞（双平台）。</summary>
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

        /// <summary>一键收藏（双平台）。</summary>
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

        /// <summary>发评论（双平台）。</summary>
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

        /// <summary>账号健康度自检。</summary>
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
