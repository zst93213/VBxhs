using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Vista.Accounts;
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

        public void NavigateTo(string tag)
        {
            Status = "已切换到：" + tag;
            // 争渡模式：不 SpeakAuto，Status + LiveRegion 自行读出
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

        // ========== 互动 ==========

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

        /// <summary>加载当前卡片的评论列表（放到 CurrentComments，争渡 Tab 到评论区即可读）。</summary>
        public async Task<bool> LoadCurrentCommentsAsync()
        {
            if (CurrentCard == null) { Status = "请先选中一张卡片"; return false; }
            try
            {
                Status = "正在加载评论...";
                var page = await _getComments.ExecuteAsync(CurrentCard.Id, null, default);
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

        // ========== 信息流 / 离线缓存 ==========

        public async Task RefreshFeedAsync()
        {
            IsRefreshing = true;
            Status = "正在刷新信息流...";
            try
            {
                var result = await _weiboFeed.ExecuteAsync(null, default);
                Cards.Clear();
                foreach (var card in result.Items) Cards.Add(card);
                Status = $"已加载 {Cards.Count} 条内容";
                OfflineMode = false;
                await SaveFeedOfflineAsync(silent: true);
            }
            catch (Exception ex)
            {
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

        /// <summary>保存信息流到本地缓存（离线查看）。silent=true 时不弹窗，供刷新成功后自动调用。</summary>
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
    }
}
