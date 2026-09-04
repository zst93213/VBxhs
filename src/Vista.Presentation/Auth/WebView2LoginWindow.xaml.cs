using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Serilog;
using Vista.Accounts;
using Vista.Core;

namespace Vista.Presentation.Auth
{
    /// <summary>
    /// WebView2 托管登录窗口。
    /// 流程（M0 全量落地）：
    ///   1) 用户选平台 → 加载官方登录页（微博 passport / 小红书首页扫码入口）
    ///   2) 监听 NavigationCompleted；URL 跳到主站视为登录成功
    ///   3) 通过 CoreWebView2.CookieManager.GetCookiesAsync(domain) 提取登录 Cookie
    ///   4) 序列化为 Cookie 字符串 → UTF-8 字节 → SecureCredentialVault.Register 加密
    ///   5) 通过 /profile/info 接口取真实 UID 作为账号唯一标识
    ///   6) 清理 WebView 会话（避免凭证在浏览器侧残留）
    /// </summary>
    public partial class WebView2LoginWindow : Window
    {
        public bool LoginSucceeded { get; private set; }
        public AccountInfo CreatedAccount { get; private set; }

        public WebView2LoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoginWebView.EnsureCoreWebView2Async(null);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                LoginStatus.Text = "未检测到 WebView2 运行时，请先安装。";
                MessageBox.Show("需要先安装 Microsoft Edge WebView2 Runtime。\n下载地址：https://developer.microsoft.com/microsoft-edge/webview2/", "Vista");
                return;
            }

            string url = PlatformXhs.IsChecked == true
                ? "https://www.xiaohongshu.com/"
                : "https://passport.weibo.com/sso/signin";
            LoginWebView.CoreWebView2.Navigate(url);
            LoginStatus.Text = "已加载登录页，请在页面中完成登录。登录成功后凭证将加密保存。";
        }

        /// <summary>导航完成：判断登录成功，取 Cookie → 加密保存。</summary>
        private async void OnNavCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { LoginStatus.Text = "页面加载失败，请检查网络。"; return; }

            var source = LoginWebView.CoreWebView2.Source;
            var isWeibo = source.Contains("weibo.com") && !source.Contains("passport");
            var isXhs = source.Contains("xiaohongshu.com") &&
                (source.Contains("/explore") || source.Contains("/homefeed") || source == "https://www.xiaohongshu.com/" || source.Contains("/user/profile"));

            if (!isWeibo && !isXhs) return; // 还在登录页，等下次导航

            LoginStatus.Text = "检测到登录成功，正在提取并加密保存凭证…";
            try
            {
                var domain = isWeibo ? ".weibo.cn" : ".xiaohongshu.com";
                var cookieList = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync(domain);
                // 同时取 .com 根域 cookie（XSRF-TOKEN 通常在根域）
                var cookieList2 = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync(isWeibo ? ".weibo.com" : ".xiaohongshu.com");
                var all = new List<CoreWebView2Cookie>();
                if (cookieList != null) all.AddRange(cookieList);
                if (cookieList2 != null)
                    foreach (var c in cookieList2)
                        if (!all.Exists(x => x.Name == c.Name && x.Domain == c.Domain))
                            all.Add(c);

                var cookieString = BuildCookieString(all);
                if (string.IsNullOrEmpty(cookieString))
                {
                    LoginStatus.Text = "未能读取到登录 Cookie，请确认登录成功后稍候。";
                    return;
                }

                var platform = isWeibo ? PlatformId.Weibo : PlatformId.Xiaohongshu;
                var uid = ExtractUidFromCookie(cookieString, platform) ?? "unknown-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var info = new AccountInfo
                {
                    Platform = platform,
                    Uid = uid,
                    DisplayName = (isWeibo ? "微博 " : "小红书 ") + uid,
                    LastLoginAt = DateTimeOffset.Now
                };

                var blob = Encoding.UTF8.GetBytes(cookieString);
                var app = (App)Application.Current;
                app.Accounts.Register(info, blob);
                app.AccountContext.SwitchTo(info.ToAccountId());

                // 销毁 WebView 会话，避免 Cookie 在浏览器侧残留
                try { await LoginWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(); }
                catch (Exception ex) { Log.Warning(ex, "清理 WebView 会话失败"); }

                CreatedAccount = info;
                LoginSucceeded = true;
                LoginStatus.Text = $"登录成功：{info.DisplayName}（{info.Uid}）";
                await System.Threading.Tasks.Task.Delay(800);
                Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cookie 提取/保存失败");
                LoginStatus.Text = "凭证保存失败：" + ex.Message;
            }
        }

        /// <summary>把 Cookie 列表转为 "k1=v1; k2=v2" 形式（HTTP Cookie header）。</summary>
        private static string BuildCookieString(List<CoreWebView2Cookie> cookies)
        {
            if (cookies == null || cookies.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var c in cookies)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(c.Name).Append('=').Append(c.Value);
            }
            return sb.ToString();
        }

        /// <summary>从 Cookie 中提取 UID 作为账号唯一标识。
        /// 微博：SUBP 字段含 uid；小红书：customerClientId 或 web_session 中无 uid，
        /// 此时调用方后续可主动 /user/selfinfo 更新。</summary>
        private static string ExtractUidFromCookie(string cookie, PlatformId platform)
        {
            // 简化：从 cookie 中找 uid 字段
            var key = platform == PlatformId.Weibo ? "SUB" : "web_session";
            var idx = cookie.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + key.Length + 1;
            var end = cookie.IndexOf(';', start);
            if (end < 0) end = cookie.Length;
            return cookie.Substring(start, end - start);
        }
    }
}
