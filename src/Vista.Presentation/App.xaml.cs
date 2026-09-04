using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Vista.Accounts;
using Vista.Accounts.Vault;
using Vista.Adapters.Weibo;
using Vista.Adapters.Xiaohongshu;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Models;
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
    /// <remarks>
    /// 启动失败诊断：用户双击 Vista.exe "什么反应都没有"，多半是 OnStartup 早期抛了未捕获异常
    /// （WPF dispatcher 还没起来，看不到错误对话框）。本类做了三件事保证崩溃可见：
    ///   1) OnStartup 整体包 try-catch，失败立刻写 startup.log + 弹窗
    ///   2) 注册 DispatcherUnhandledException / AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException
    ///   3) 启动早期写一份 startup.log（先于 SerilogBootstrap，确保哪怕 Serilog 也崩时有日志）
    /// </remarks>
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

            // 第 -1 步：注册全局未处理异常钩子（最早做，确保后续任何阶段崩溃都能被捕获）
            RegisterGlobalExceptionHandlers();

            // 第 0 步：写启动日志（独立于 Serilog，避免 Serilog 自身初始化失败时拿不到诊断）
            WriteStartupLog("Vista 启动开始");

            try
            {
                Bootstrap();
                WriteStartupLog("Vista 启动完成，主窗口已显示");
            }
            catch (Exception ex)
            {
                // 启动失败：写日志 + 弹窗（让用户至少能看到错误信息，不会"什么反应都没有"）
                var msg = "Vista 启动失败：" + ex.GetType().Name + " — " + ex.Message + "\r\n\r\n" + ex.StackTrace;
                WriteStartupLog("FATAL: " + msg + "\r\n\r\n" + ex.ToString());
                try { Log.Fatal(ex, "Vista 启动失败"); } catch { /* Serilog 可能还没初始化或也崩了 */ }
                MessageBox.Show(msg, "Vista 启动失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>实际装配逻辑（与原 OnStartup 等价，单独抽出以便整体 try-catch）。</summary>
        private void Bootstrap()
        {
            // 第 1 步：争渡读屏兼容配置（先做，影响后续 NarrationService 开关等）
            try
            {
                Accessibility.ZdsCompatibility.Apply();
                WriteStartupLog("  ZdsCompatibility.Apply 完成");
            }
            catch (Exception ex)
            {
                // 争渡适配失败不应阻塞启动，继续往下走
                WriteStartupLog("  ZdsCompatibility.Apply 失败（忽略，继续）：" + ex.Message);
            }

            // 第 2 步：Serilog
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vista", "logs");
            try
            {
                SerilogBootstrap.Configure(logDir);
                Log.Information("Vista 启动中…");
                WriteStartupLog("  SerilogBootstrap.Configure 完成");
            }
            catch (Exception ex)
            {
                // Serilog 初始化失败不应阻塞启动（用户至少还能靠 startup.log 诊断）
                WriteStartupLog("  SerilogBootstrap.Configure 失败（忽略，继续）：" + ex);
            }

            // 第 3 步：账号仓库 + 凭证保险箱（DPAPI）
            var vault = new SecureCredentialVault();
            Accounts = new AccountRepository(vault);
            AccountContext = new AccountContext();
            WriteStartupLog("  AccountRepository / AccountContext 完成");

            // 第 4 步：持久化缓存（SQLite，§九）；如果 SQLite 不可用降级到 FileCacheStore
            try { Cache = new SQLiteCacheStore(Path.Combine(LocalDir, "cache.db")); }
            catch (Exception ex) { Log.Warning(ex, "SQLite 缓存初始化失败，回退到 FileCacheStore"); Cache = new FileCacheStore(); }
            WriteStartupLog("  Cache 完成");

            // 第 5 步：每账号独立的 ResilientHttpClient（防关联，§五）
            var clientFactory = BuildClientFactory();

            // 第 6 步：真实 Adapter：微博直接用 Cookie；小红书注入签名器（默认空签名器，写入操作优雅失败）
            WeiboAdapter = new WeiboAdapter(Accounts, clientFactory);
            XiaohongshuAdapter = new XiaohongshuAdapter(Accounts, clientFactory, new NullXhsSignatureProvider());
            WriteStartupLog("  WeiboAdapter / XiaohongshuAdapter 完成");

            // 注：CachedCrawlerAdapter 用于装饰 ICrawlerAdapter，提供读取接口的缓存。
            // 主流程暂时直接用具体 Adapter（写入接口共享），缓存通过 ICacheStore 在 ViewModel 显式调用。
            // 后续可把 Adapter 装饰成 CachedCrawlerAdapter 获得自动缓存。

            // 第 7 步：ViewModel 注入双平台用例 + 平台切换路由
            MainWindowVm = new MainViewModel(
                new GetHomeTimelineUseCase(WeiboAdapter, AccountContext),
                new SearchNotesUseCase(XiaohongshuAdapter, AccountContext),
                new RepostUseCase(WeiboAdapter, AccountContext),
                new ShareNoteUseCase(XiaohongshuAdapter, AccountContext),
                new GetCommentsUseCase(XiaohongshuAdapter, AccountContext),
                AccountContext, Accounts, Cache);
            MainWindowVm.SetAdapters(WeiboAdapter, XiaohongshuAdapter);
            MainWindowVm.ReloadAccounts();
            WriteStartupLog("  MainViewModel 装配完成");

            // 第 8 步：主窗口
            var main = new MainWindow { DataContext = MainWindowVm };
            main.Show();
        }

        /// <summary>注册全局未处理异常钩子，确保任何线程、任何阶段的崩溃都能被记录与显示。</summary>
        private void RegisterGlobalExceptionHandlers()
        {
            // UI 线程未处理异常
            DispatcherUnhandledException += (s, args) =>
            {
                var ex = args.Exception;
                var msg = "Vista 遇到未处理异常：\r\n\r\n" + ex.GetType().Name + " — " + ex.Message
                          + "\r\n\r\n" + ex.StackTrace;
                WriteStartupLog("UI 未处理异常: " + ex);
                try { Log.Fatal(ex, "DispatcherUnhandledException"); } catch { }
                MessageBox.Show(msg, "Vista 运行错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; // 阻止默认崩溃，让用户能保存状态
            };

            // 非 UI 线程（后台线程、.finalizer、native callback）未处理异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                WriteStartupLog("AppDomain 未处理异常: " + (ex?.ToString() ?? args.ExceptionObject?.ToString() ?? "<null>"));
                try { Log.Fatal(ex, "AppDomain.UnhandledException"); } catch { }
                // 此处无法阻止进程终止；至少把日志写下来
            };

            // Task 未观察异常（async void / fire-and-forget Task）
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                WriteStartupLog("Task 未观察异常: " + args.Exception);
                try { Log.Error(args.Exception, "UnobservedTaskException"); } catch { }
                args.SetObserved(); // 标记已观察，阻止进程崩溃
            };
        }

        /// <summary>写一行启动日志到 %LOCALAPPDATA%\Vista\startup.log（独立于 Serilog）。</summary>
        public static void WriteStartupLog(string message)
        {
            try
            {
                var path = Path.Combine(LocalDir, "startup.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + "\r\n");
            }
            catch { /* 启动诊断自身不能让 App 崩 */ }
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
