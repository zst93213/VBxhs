using System;
using System.Windows;
using Vista.Core;

namespace Vista.Presentation.Auth
{
    /// <summary>
    /// WebView2 托管登录窗口。设计计划：登录态用 WebView 托管登录（合规风险最低）。
    /// M0 demo：加载官方登录页 + 监听导航完成；M1 在 NavigationCompleted 中
    /// 通过 CoreWebView2.CookieManager 读取登录 Cookie，送入 DPAPI 保险箱后销毁 WebView 会话。
    /// </summary>
    public partial class WebView2LoginWindow : Window
    {
        public WebView2LoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 1. 初始化 WebView2 运行时
            await LoginWebView.EnsureCoreWebView2Async(null);

            // 2. 按所选平台加载官方登录页
            string url = PlatformXhs.IsChecked == true
                ? "https://www.xiaohongshu.com/"     // 小红书：站点首页即含登录入口
                : "https://passport.weibo.com/sso/signin"; // 微博：passport 登录页
            LoginWebView.CoreWebView2.Source = new Uri(url);

            LoginStatus.Text = "已加载登录页，请在页面中完成登录。登录成功后凭证将加密保存。";
        }

        /// <summary>导航完成：判断是否已登录（URL 跳到主站 = 登录成功），M1 在此取 Cookie。</summary>
        private async void OnNavCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { LoginStatus.Text = "页面加载失败，请检查网络。"; return; }

            var source = LoginWebView.CoreWebView2.Source;
            bool onMainSite = source.Contains("weibo.com") && !source.Contains("passport")
                           || source.Contains("xiaohongshu.com") && source.Contains("/explore");

            if (onMainSite)
            {
                LoginStatus.Text = "检测到登录成功，正在提取并加密保存凭证…";
                // M1 实现：
                //   var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync(domain);
                //   var blob = Serialize(cookies);
                //   ((App)Application.Current).Accounts.Register(info, blob);
                //   LoginWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(); // 销毁会话
                await System.Threading.Tasks.Task.Delay(500); // 占位
                LoginStatus.Text = "登录完成（M1 将在此加密保存凭证）。";
                MessageBox.Show("登录完成。M1 将实现 Cookie 提取与加密存储。", "Vista");
                Close();
            }
        }
    }
}
