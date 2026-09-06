using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vista.Adapters.Weibo
{
    /// <summary>
    /// 微博扫码登录服务（不依赖 WebView2，纯 HTTP + 二维码图片）。
    /// 流程：
    ///   1) GET https://login.sina.com.cn/sso/qrcode/image?entry=weibo&size=180
    ///      → 返回 { data: { image: "https://...", qrid: "xxx" } }
    ///   2) 下载 image 字节流 → 调用方用 Image 控件显示二维码
    ///   3) 轮询 https://login.sina.com.cn/sso/qrcode/check?entry=weibo&qrid=xxx
    ///      → ret: 500 未扫码 / 200 已扫码 / 201 已确认（带 ticket）
    ///   4) 用 ticket 换 cookie：GET https://passport.weibo.com/sso/vip/crossdomain?entry=weibo&ticket=xxx
    ///      → 响应 Set-Cookie 含 SUB、SUBP、XSRF-TOKEN、SUHB
    ///   5) 拼接 cookie 字符串返回（UTF-8 字节，由调用方加密存储）
    /// </summary>
    public sealed class WeiboQrCodeLoginService : IDisposable
    {
        private const string QrImageUrl = "https://login.sina.com.cn/sso/qrcode/image?entry=weibo&size=180";
        private const string QrCheckUrl = "https://login.sina.com.cn/sso/qrcode/check?entry=weibo&qrid=";
        private const string CrossDomainUrl = "https://passport.weibo.com/sso/vip/crossdomain?entry=weibo&ticket=";

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _http;

        public WeiboQrCodeLoginService()
        {
            // 必须启用 CookieContainer：crossdomain 接口会通过 Set-Cookie 写入登录凭证
            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(_handler);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
        }

        /// <summary>
        /// 生成登录二维码。返回（二维码图片字节, qrid）。
        /// 调用方把字节流喂给 Image 控件显示；用 qrid 轮询扫码状态。
        /// </summary>
        public async Task<(byte[] imageBytes, string qrid)> GenerateQrCodeAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await GetStringAsync(QrImageUrl, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                var qrid = data.GetProperty("qrid").GetString();
                var imageUrl = data.GetProperty("image").GetString();

                if (string.IsNullOrEmpty(qrid) || string.IsNullOrEmpty(imageUrl))
                    throw new InvalidOperationException("二维码接口返回数据缺失");

                // image 字段是 //login.sina.com.cn/... 开头，补 https:
                if (imageUrl.StartsWith("//")) imageUrl = "https:" + imageUrl;

                var bytes = await GetByteArrayAsync(imageUrl, ct).ConfigureAwait(false);
                return (bytes, qrid);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("获取微博登录二维码失败：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// 轮询扫码状态。返回：
        ///   "pending" 未扫码
        ///   "scanned" 已扫码未确认
        ///   "confirmed:{ticket}" 已确认（带 ticket）
        /// </summary>
        public async Task<string> CheckQrStatusAsync(string qrid, CancellationToken ct = default)
        {
            try
            {
                var json = await GetStringAsync(QrCheckUrl + Uri.EscapeDataString(qrid), ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var ret = doc.RootElement.GetProperty("ret").GetInt32();
                switch (ret)
                {
                    case 500: return "pending";
                    case 200: return "scanned";
                    case 201:
                        // 已确认，data.ticket 字段是换取 cookie 的票据
                        if (doc.RootElement.TryGetProperty("data", out var data)
                            && data.TryGetProperty("ticket", out var ticket))
                            return "confirmed:" + ticket.GetString();
                        return "confirmed:";
                    default: return "error:" + ret;
                }
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        /// <summary>
        /// 用 ticket 换取登录 Cookie 字符串。返回 "k1=v1; k2=v2" 形式。
        /// crossdomain 接口通过 Set-Cookie 写入 SUB / SUBP / XSRF-TOKEN / SUHB 等。
        /// </summary>
        public async Task<string> ExchangeTicketForCookiesAsync(string ticket, CancellationToken ct = default)
        {
            try
            {
                // 发起请求，让 crossdomain 把 cookie 写入 CookieContainer
                var resp = await _http.GetAsync(CrossDomainUrl + Uri.EscapeDataString(ticket), ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                // 从 CookieContainer 取 .weibo.cn 和 .weibo.com 和 .sina.com.cn 域的所有 cookie
                var all = new List<Cookie>();
                all.AddRange(_handler.CookieContainer.GetCookies(new Uri("https://.weibo.cn")).Cast<Cookie>());
                all.AddRange(_handler.CookieContainer.GetCookies(new Uri("https://.weibo.com")).Cast<Cookie>());
                all.AddRange(_handler.CookieContainer.GetCookies(new Uri("https://.sina.com.cn")).Cast<Cookie>());
                // 去重（同名同域）
                var seen = new HashSet<string>();
                var sb = new StringBuilder();
                foreach (var c in all)
                {
                    var key = c.Domain + "|" + c.Name;
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(c.Name).Append('=').Append(c.Value);
                }
                var cookie = sb.ToString();
                if (string.IsNullOrEmpty(cookie) || !cookie.Contains("SUB="))
                    throw new InvalidOperationException("未能从 crossdomain 响应中提取到登录 Cookie（缺少 SUB）");
                return cookie;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("换取微博登录 Cookie 失败：" + ex.Message, ex);
            }
        }

        public void Dispose()
        {
            _http?.Dispose();
            _handler?.Dispose();
        }

        // .NET Framework 4.8.1 的 HttpClient.GetStringAsync / GetByteArrayAsync 不接受 CancellationToken，
        // 这里用 GetAsync + 取消令牌包装，保证登录窗口可被用户关闭时及时中止。
        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private async Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    }
}
