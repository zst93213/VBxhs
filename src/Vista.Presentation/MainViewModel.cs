using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Vista.Accounts;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;
using Vista.Features.Weibo.UseCases;
using Vista.Features.Xiaohongshu.UseCases;
using Vista.Infrastructure.Cache;

namespace Vista.Presentation
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly GetHomeTimelineUseCase _weiboFeed;
        private readonly SearchNotesUseCase _xhsSearch;
        private readonly RepostUseCase _repost;
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
            RepostUseCase repost,
            AccountContext accountContext,
            AccountRepository accounts,
            ICacheStore cache)
        {
            _weiboFeed = weiboFeed;
            _xhsSearch = xhsSearch;
            _repost = repost;
            _accountContext = accountContext;
            _accounts = accounts;
            _cache = cache;
        }

        public void NavigateTo(string tag)
        {
            Status = "已切换到：" + tag;
        }

        public void NarrateCurrentCard()
        {
            if (CurrentCard == null)
            {
                Status = "当前无卡片可朗读";
                return;
            }
            Accessibility.NarrationService.Speak(CurrentCard.SpokenLabel);
            Status = "正在朗读：" + CurrentCard.AuthorName;
        }

        public void NarrateComments()
        {
            if (CurrentComments.Count == 0)
            {
                Status = "当前评论区为空，或尚未加载";
                Accessibility.NarrationService.Speak("当前没有评论");
                return;
            }
            Accessibility.NarrationService.SpeakComments(CurrentComments);
            Status = $"正在朗读 {CurrentComments.Count} 条评论";
        }

        public async Task<bool> RepostCurrentCardAsync()
        {
            if (CurrentCard == null)
            {
                Status = "当前无卡片可转发";
                return false;
            }
            try
            {
                Status = "正在转发：" + CurrentCard.AuthorName;
                var ok = await _repost.ExecuteAsync(CurrentCard.Id);
                Status = ok ? "转发成功" : "转发失败";
                if (ok)
                    Accessibility.NarrationService.Speak("转发成功");
                return ok;
            }
            catch (Exception ex)
            {
                Status = "转发出错：" + ex.Message;
                Accessibility.NarrationService.Speak("转发失败");
                return false;
            }
        }

        public async Task RefreshFeedAsync()
        {
            IsRefreshing = true;
            Status = "正在刷新信息流...";
            try
            {
                var result = await _weiboFeed.ExecuteAsync(null, default);
                Cards.Clear();
                foreach (var card in result.Items)
                    Cards.Add(card);
                Status = $"已加载 {Cards.Count} 条内容";
                OfflineMode = false;
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
        }

        private async Task<bool> TryLoadOfflineAsync()
        {
            try
            {
                var offline = await _cache.GetAsync<PagedResult<PostCard>>("offline:home");
                if (offline?.Items == null || offline.Items.Count == 0) return false;
                Cards.Clear();
                foreach (var card in offline.Items)
                    Cards.Add(card);
                Status = $"离线模式：已加载 {Cards.Count} 条缓存内容";
                return true;
            }
            catch { return false; }
        }

        public async Task SaveFeedOfflineAsync()
        {
            if (Cards.Count == 0) { Status = "没有可缓存的内容"; return; }
            try
            {
                var result = new PagedResult<PostCard>(Cards.ToList(), null);
                await _cache.SetAsync("offline:home", result, TimeSpan.FromDays(7));
                Status = $"已离线缓存 {Cards.Count} 条内容（7 天有效）";
                Accessibility.NarrationService.Speak($"已缓存 {Cards.Count} 条内容");
            }
            catch (Exception ex)
            {
                Status = "缓存失败：" + ex.Message;
            }
        }
    }
}
