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
                AccountContext, Accounts, Cache);

            var main = new MainWindow { DataContext = MainWindowVm };
            main.Show();
        }

        public MainViewModel MainWindowVm { get; private set; }
    }
}
