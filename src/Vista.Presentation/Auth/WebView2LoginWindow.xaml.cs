using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Serilog;
using Vista.Accounts;
using Vista.Adapters.Weibo;
using Vista.Core;

namespace Vista.Presentation.Auth
{
    /// <summary>
    /// 微博扫码登录窗口（不使用 WebView2，纯二维码图片 + HTTP 轮询）。
    /// 流程：
    ///   1) Loaded 时调用 WeiboQrCodeLoginService.GenerateQrCodeAsync 获取二维码图片字节 → Image 显示
    ///   2) 启动 DispatcherTimer 每 2 秒轮询 CheckQrStatusAsync
    ///   3) 状态 confirmed:{ticket} 时调 ExchangeTicketForCookiesAsync 拿 Cookie
    ///   4) Cookie 转 UTF-8 字节 → SecureCredentialVault.Register 加密保存
    ///   5) 调 /profile/info 取真实 UID 作为账号标识
    /// </summary>
    public partial class WebView2LoginWindow : Window
    {
        private WeiboQrCodeLoginService _loginService;
        private DispatcherTimer _pollTimer;
        private string _qrid;
        private bool _completed;

        public bool LoginSucceeded { get; private set; }
        public AccountInfo CreatedAccount { get; private set; }

        public WebView2LoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loginService = new WeiboQrCodeLoginService();
            await RefreshQrAsync();

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pollTimer.Tick += async (s, args) => await PollStatusAsync();
            _pollTimer.Start();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _pollTimer?.Stop();
            _loginService?.Dispose();
        }

        /// <summary>重新生成二维码。</summary>
        private async void OnRefreshQr(object sender, RoutedEventArgs e) => await RefreshQrAsync();

        private async System.Threading.Tasks.Task RefreshQrAsync()
        {
            try
            {
                RefreshQrBtn.IsEnabled = false;
                LoginStatus.Text = "正在生成二维码...";
                var (imageBytes, qrid) = await _loginService.GenerateQrCodeAsync();
                _qrid = qrid;

                // 字节流 → BitmapImage → Image.Source
                using var ms = new MemoryStream(imageBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                QrImage.Source = bmp;

                LoginStatus.Text = "请用手机微博扫描二维码登录";
                RefreshQrBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "生成二维码失败");
                LoginStatus.Text = "生成二维码失败：" + ex.Message;
                RefreshQrBtn.IsEnabled = true;
            }
        }

        /// <summary>轮询扫码状态。</summary>
        private async System.Threading.Tasks.Task PollStatusAsync()
        {
            if (_completed || string.IsNullOrEmpty(_qrid)) return;
            try
            {
                var status = await _loginService.CheckQrStatusAsync(_qrid);
                if (status == "pending")
                {
                    // 未扫码，继续等
                }
                else if (status == "scanned")
                {
                    LoginStatus.Text = "已扫码，请在手机上确认登录";
                }
                else if (status.StartsWith("confirmed:"))
                {
                    var ticket = status.Substring("confirmed:".Length);
                    await CompleteLoginAsync(ticket);
                }
                else if (status == "expired")
                {
                    LoginStatus.Text = "二维码已过期，正在刷新...";
                    await RefreshQrAsync();
                }
                else if (status.StartsWith("error:"))
                {
                    LoginStatus.Text = "状态检查异常：" + status;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "轮询扫码状态失败");
            }
        }

        /// <summary>扫码确认后用 ticket 换 cookie → 加密保存。</summary>
        private async System.Threading.Tasks.Task CompleteLoginAsync(string ticket)
        {
            if (_completed) return;
            _completed = true;
            _pollTimer?.Stop();

            try
            {
                LoginStatus.Text = "登录成功，正在保存凭证...";
                var cookie = await _loginService.ExchangeTicketForCookiesAsync(ticket);

                // 取 UID：优先从 SUBP cookie 解析，失败则调 /profile/info
                var uid = ExtractUidFromSubp(cookie);
                if (string.IsNullOrEmpty(uid))
                {
                    // 调 profile/info 拿真实 UID
                    uid = await FetchUidAsync(cookie);
                }
                if (string.IsNullOrEmpty(uid))
                    uid = "weibo-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                var info = new AccountInfo
                {
                    Platform = PlatformId.Weibo,
                    Uid = uid,
                    DisplayName = "微博 " + uid,
                    LastLoginAt = DateTimeOffset.Now
                };

                var blob = Encoding.UTF8.GetBytes(cookie);
                var app = (App)Application.Current;
                app.Accounts.Register(info, blob);
                app.AccountContext.SwitchTo(info.ToAccountId());

                CreatedAccount = info;
                LoginSucceeded = true;
                LoginStatus.Text = $"登录成功：{info.DisplayName}";
                await System.Threading.Tasks.Task.Delay(600);
                Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "登录凭证保存失败");
                LoginStatus.Text = "凭证保存失败：" + ex.Message;
                _completed = false; // 允许重试
                _pollTimer?.Start();
            }
        }

        /// <summary>从 SUBP cookie 中提取 UID。SUBP 格式：0033WrYD...&uid=123456&... </summary>
        private static string ExtractUidFromSubp(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) return null;
            var idx = cookie.IndexOf("SUBP=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + "SUBP=".Length;
            var end = cookie.IndexOf(';', start);
            if (end < 0) end = cookie.Length;
            var subp = cookie.Substring(start, end - start);
            var uidIdx = subp.IndexOf("uid=", StringComparison.OrdinalIgnoreCase);
            if (uidIdx < 0) return null;
            var uidStart = uidIdx + 4;
            var uidEnd = subp.IndexOf('&', uidStart);
            if (uidEnd < 0) uidEnd = subp.Length;
            return subp.Substring(uidStart, uidEnd - uidStart);
        }

        /// <summary>通过 /profile/info 接口获取当前登录用户 UID。</summary>
        private async System.Threading.Tasks.Task<string> FetchUidAsync(string cookie)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    "https://m.weibo.cn/api/config");
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                req.Headers.TryAddWithoutValidation("Referer", "https://m.weibo.cn/");
                using var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data)
                    && data.TryGetProperty("uid", out var uid))
                    return uid.GetRawText().Trim('"');
                return null;
            }
            catch { return null; }
        }
    }
}
