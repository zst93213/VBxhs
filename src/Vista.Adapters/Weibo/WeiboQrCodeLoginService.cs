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
    ///
    /// 完整流程（参考 huiyidanli/SinaLogin）：
    ///   1) GET https://login.sina.com.cn/sso/qrcode/image?entry=weibo&size=180
    ///      → { "retcode": 20000000, "data": { "image": "//login.sina.com.cn/...", "qrid": "xxx" } }
    ///   2) 下载 image 字节流 → Image 控件显示
    ///   3) 轮询 https://login.sina.com.cn/sso/qrcode/check?entry=weibo&qrid=xxx
    ///      → retcode: 50114001 未扫码 / 50114002 已扫码 / 0 已确认(带 alt 票据) / 50114004 已过期
    ///   4) 用 alt 票据换 cookie：
    ///      GET https://login.sina.com.cn/sso/crossdomain?entry=weibo&ticket={alt}
    ///      → 返回 crossDomainUrlList，逐个访问收集 Set-Cookie
    ///   5) 拼接 cookie 字符串返回
    /// </summary>
    public sealed class WeiboQrCodeLoginService : IDisposable
    {
        private const string QrImageUrl = "https://login.sina.com.cn/sso/qrcode/image?entry=weibo&size=180";
        private const string QrCheckUrl = "https://login.sina.com.cn/sso/qrcode/check?entry=weibo&qrid=";
        // crossdomain 在 login.sina.com.cn，会返回需要访问的跨域 URL 列表
        private const string CrossDomainUrl = "https://login.sina.com.cn/sso/crossdomain?entry=weibo&ticket=";

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _http;

        public WeiboQrCodeLoginService()
        {
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
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            _http.DefaultRequestHeaders.Add("Referer", "https://login.sina.com.cn/");
        }

        /// <summary>
        /// 生成登录二维码。返回（二维码图片字节, qrid）。
        /// </summary>
        public async Task<(byte[] imageBytes, string qrid)> GenerateQrCodeAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await GetStringAsync(QrImageUrl, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                // API 返回 retcode=20000000 表示成功
                var retcode = GetRetcode(doc.RootElement);
                if (retcode != 20000000 && retcode != 0)
                    throw new InvalidOperationException($"二维码接口返回错误 retcode={retcode}");

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
        ///   "pending"    未扫码
        ///   "scanned"    已扫码未确认
        ///   "confirmed:{alt}"  已确认（alt 是跨域票据）
        ///   "expired"    二维码已过期
        ///   "error:{msg}" 其他错误
        /// </summary>
        public async Task<string> CheckQrStatusAsync(string qrid, CancellationToken ct = default)
        {
            try
            {
                var json = await GetStringAsync(QrCheckUrl + Uri.EscapeDataString(qrid), ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var retcode = GetRetcode(doc.RootElement);

                // 新版 API 状态码
                switch (retcode)
                {
                    case 50114001: return "pending";      // 等待扫码
                    case 50114002: return "scanned";       // 已扫码待确认
                    case 0:                               // 已确认
                        if (doc.RootElement.TryGetProperty("data", out var data))
                        {
                            // alt 字段是跨域票据
                            var alt = TryGetString(data, "alt");
                            if (!string.IsNullOrEmpty(alt))
                                return "confirmed:" + alt;
                        }
                        return "confirmed:";
                    case 50114004: return "expired";      // 二维码过期
                    default:
                        // 兼容旧版 API 的 ret 字段（500/200/201）
                        var ret = GetRet(doc.RootElement);
                        if (ret >= 0)
                        {
                            switch (ret)
                            {
                                case 500: return "pending";
                                case 200: return "scanned";
                                case 201:
                                    if (doc.RootElement.TryGetProperty("data", out var data2))
                                    {
                                        var ticket = TryGetString(data2, "ticket") ?? TryGetString(data2, "alt");
                                        if (!string.IsNullOrEmpty(ticket))
                                            return "confirmed:" + ticket;
                                    }
                                    return "confirmed:";
                                default: return "error:retcode=" + retcode + ",ret=" + ret;
                            }
                        }
                        return "error:retcode=" + retcode;
                }
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        /// <summary>
        /// 用 alt/ticket 票据换取登录 Cookie 字符串。
        /// 步骤：
        ///   1) 访问 crossdomain 接口，拿到跨域 URL 列表
        ///   2) 逐个访问跨域 URL，CookieContainer 自动收集 Set-Cookie
        ///   3) 从 CookieContainer 中提取 .weibo.cn / .weibo.com / .sina.com.cn 域的 cookie
        /// </summary>
        public async Task<string> ExchangeTicketForCookiesAsync(string ticket, CancellationToken ct = default)
        {
            try
            {
                // 第一步：访问 crossdomain 获取跨域 URL 列表
                var crossJson = await GetStringAsync(CrossDomainUrl + Uri.EscapeDataString(ticket), ct).ConfigureAwait(false);

                // 解析跨域 URL 列表
                var crossUrls = new List<string>();
                try
                {
                    using var doc = JsonDocument.Parse(crossJson);
                    var rc = GetRetcode(doc.RootElement);
                    if (rc != 0 && rc != 20000000)
                    {
                        // 如果 crossdomain 返回错误，也尝试继续
                    }
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("crossDomainUrlList", out var list))
                        {
                            foreach (var u in list.EnumerateArray())
                            {
                                var url = u.GetString();
                                if (!string.IsNullOrEmpty(url)) crossUrls.Add(url);
                            }
                        }
                        // 有些版本用 JSONArray 字段名不同
                        if (crossUrls.Count == 0)
                        {
                            foreach (var prop in data.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var u in prop.Value.EnumerateArray())
                                    {
                                        var url = u.GetString();
                                        if (!string.IsNullOrEmpty(url)) crossUrls.Add(url);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // 第二步：逐个访问跨域 URL，让 CookieContainer 收集 cookie
                if (crossUrls.Count > 0)
                {
                    foreach (var url in crossUrls)
                    {
                        try
                        {
                            await _http.GetAsync(url, ct).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
                else
                {
                    // 没有跨域列表，直接访问 passport.weibo.com 拿 cookie
                    try
                    {
                        await _http.GetAsync(
                            "https://passport.weibo.com/sso/vip/crossdomain?entry=weibo&ticket=" +
                            Uri.EscapeDataString(ticket), ct).ConfigureAwait(false);
                    }
                    catch { }
                    // 再访问 m.weibo.cn 首页，让 SUB 等 cookie 写入 .weibo.cn 域
                    try
                    {
                        await _http.GetAsync("https://m.weibo.cn/", ct).ConfigureAwait(false);
                    }
                    catch { }
                }

                // 第三步：从 CookieContainer 提取所有域的 cookie
                var all = new List<Cookie>();
                var domains = new[]
                {
                    "https://m.weibo.cn/",
                    "https://weibo.cn/",
                    "https://weibo.com/",
                    "https://passport.weibo.com/",
                    "https://login.sina.com.cn/",
                    "https://sina.com.cn/"
                };
                foreach (var d in domains)
                {
                    try
                    {
                        var cookies = _handler.CookieContainer.GetCookies(new Uri(d));
                        foreach (Cookie c in cookies) all.Add(c);
                    }
                    catch { }
                }

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
                    throw new InvalidOperationException(
                        "未能提取到登录 Cookie（缺少 SUB），crossdomain 返回：" +
                        crossJson.Substring(0, Math.Min(200, crossJson.Length)));

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

        // ========== 辅助方法 ==========

        /// <summary>兼容 retcode 和 ret 字段，优先 retcode。</summary>
        private static int GetRetcode(JsonElement el)
        {
            if (el.TryGetProperty("retcode", out var rc) && rc.ValueKind == JsonValueKind.Number)
                return rc.GetInt32();
            return -1;
        }

        private static int GetRet(JsonElement el)
        {
            if (el.TryGetProperty("ret", out var r) && r.ValueKind == JsonValueKind.Number)
                return r.GetInt32();
            return -1;
        }

        private static string TryGetString(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            // 不用 EnsureSuccessStatusCode，让调用方看到响应体中的错误信息
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
