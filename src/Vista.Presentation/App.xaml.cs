using System;
using System.Windows;
using Vista.Accounts;
using Vista.Adapters.Weibo;
using Vista.Adapters.Xiaohongshu;
using Vista.Core.Adapters;
using Vista.Core.Models;
using Vista.Features.Weibo.UseCases;
using Vista.Features.Xiaohongshu.UseCases;
using Vista.Infrastructure.Cache;
using Vista.Infrastructure.Http;
using Vista.Infrastructure.Logging;

using Vista.Features.Xiaohongshu.UseCases;

namespace Vista.Presentation
{
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
            Vista.Presentation.Accessibility.ZdsCompatibility.Apply();

            var logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                + "\\Vista\\logs";
            System.IO.Directory.CreateDirectory(logDir);
            SerilogBootstrap.Configure(logDir);

            var vault = new SecureCredentialVault();
            Accounts = new AccountRepository(vault);
            AccountContext = new AccountContext();

            Cache = new FileCacheStore();

            Func<AccountId, ResilientHttpClient> clientFactory = id =>
            {
                var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Vista/0.1 (" + id.Platform + ")");
                return new ResilientHttpClient(http, new RateLimitBucket(capacity: 5, refillPerSec: 1));
            };

            WeiboAdapter = new WeiboAdapter(Accounts, clientFactory);
            XiaohongshuAdapter = new XiaohongshuAdapter(Accounts, clientFactory);

            MainWindowVm = new MainViewModel(
                new GetHomeTimelineUseCase(WeiboAdapter, AccountContext),
                new SearchNotesUseCase(XiaohongshuAdapter, AccountContext),
                new RepostUseCase(WeiboAdapter, AccountContext),
                new ShareNoteUseCase(XiaohongshuAdapter, AccountContext),
                new GetCommentsUseCase(XiaohongshuAdapter, AccountContext),
                AccountContext, Accounts, Cache);

            var main = new MainWindow { DataContext = MainWindowVm };
            main.Show();
        }

        public MainViewModel MainWindowVm { get; private set; }
    }
}
