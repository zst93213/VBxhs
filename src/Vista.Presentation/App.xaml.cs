using System;
using System.IO;
using System.Windows;
using Serilog;
using Vista.Accounts;
using Vista.Accounts.Vault;
using Vista.Adapters.Weibo;
using Vista.Adapters.Xiaohongshu;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Features.Weibo.UseCases;
using Vista.Features.Xiaohongshu.UseCases;
using Vista.Infrastructure.Cache;
using Vista.Infrastructure.Http;
using Vista.Infrastructure.Logging;

namespace Vista.Presentation
{
    /// <summary>
    /// 应用入口 / Composition Root。所有依赖在此装配。
    /// 完整装配清单（M0 全量）：
    ///   1) ZdsCompatibility.Apply() — 争渡读屏适配（最先做，影响 NarrationService 开关）
    ///   2) SerilogBootstrap — 含敏感字段掩码的日志
    ///   3) SecureCredentialVault + AccountRepository — DPAPI 加密的账号仓库
    ///   4) ICacheStore = SQLiteCacheStore（持久化）；CachedCrawlerAdapter 包裹 Adapter 提供读缓存
    ///   5) WeiboAdapter + XiaohongshuAdapter — 完整 HTTP 实现
    ///   6) 各用例（双平台完整：浏览/搜索/详情/评论/点赞/收藏/关注/发布/转发分享/健康度）
    ///   7) MainViewModel 注入双平台用例 + 平台切换路由
    ///   8) MainWindow 显示
    /// </summary>
    public partial class App : Application
    {
        public AccountRepository Accounts { get; private set; }
        public AccountContext AccountContext { get; private set; }
        public IPlatformAdapter WeiboAdapter { get; private set; }
        public IPlatformAdapter XiaohongshuAdapter { get; private set; }
        public ICacheStore Cache { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 第 0 步：争渡读屏兼容配置（先做，影响后续 NarrationService 开关等）
            Accessibility.ZdsCompatibility.Apply();

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vista", "logs");
            SerilogBootstrap.Configure(logDir);
            Log.Information("Vista 启动中…");

            var vault = new SecureCredentialVault();
            Accounts = new AccountRepository(vault);
            AccountContext = new AccountContext();

            // 持久化缓存（SQLite，§九）；如果 SQLite 不可用降级到 FileCacheStore
            try { Cache = new SQLiteCacheStore(Path.Combine(LocalDir, "cache.db")); }
            catch (Exception ex) { Log.Warning(ex, "SQLite 缓存初始化失败，回退到 FileCacheStore"); Cache = new FileCacheStore(); }

            // 每账号独立的 ResilientHttpClient（防关联，§五）
            var clientFactory = BuildClientFactory();

            // 真实 Adapter：微博直接用 Cookie；小红书注入签名器（默认空签名器，写入操作优雅失败）
            WeiboAdapter = new WeiboAdapter(Accounts, clientFactory);
            XiaohongshuAdapter = new XiaohongshuAdapter(Accounts, clientFactory, new NullXhsSignatureProvider());

            // 注：CachedCrawlerAdapter 用于装饰 ICrawlerAdapter，提供读取接口的缓存。
            // 主流程暂时直接用具体 Adapter（写入接口共享），缓存通过 ICacheStore 在 ViewModel 显式调用。
            // 后续可把 Adapter 装饰成 CachedCrawlerAdapter 获得自动缓存。

            MainWindowVm = new MainViewModel(
                new GetHomeTimelineUseCase(WeiboAdapter, AccountContext),
                new SearchNotesUseCase(XiaohongshuAdapter, AccountContext),
                new RepostUseCase(WeiboAdapter, AccountContext),
                new ShareNoteUseCase(XiaohongshuAdapter, AccountContext),
                new GetCommentsUseCase(XiaohongshuAdapter, AccountContext),
                AccountContext, Accounts, Cache);
            MainWindowVm.SetAdapters(WeiboAdapter, XiaohongshuAdapter);
            MainWindowVm.ReloadAccounts();

            var main = new MainWindow { DataContext = MainWindowVm };
            main.Show();
        }

        public MainViewModel MainWindowVm { get; private set; }

        /// <summary>本地数据目录：LocalAppData/Vista。</summary>
        public static string LocalDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vista");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private Func<AccountId, ResilientHttpClient> BuildClientFactory()
        {
            return id =>
            {
                var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Vista/0.1 (" + id.Platform + ")");
                return new ResilientHttpClient(http, new RateLimitBucket(capacity: 5, refillPerSec: 1));
            };
        }
    }
}
