using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;
using Vista.Infrastructure.Http;

namespace Vista.Adapters.Xiaohongshu
{
    /// <summary>
    /// 小红书适配器。基于 edith.xiaohongshu.com API（参考 Spider_XHS / Xiaohongshu-API）。
    /// 设计要点：
    ///   1) 每个 AccountId 独立 HttpClient + 限速桶（防关联，§五）。
    ///   2) 凭证 = WebView2 登录后从浏览器取出的 Cookie 字符串（UTF-8 字节）。
    ///   3) 所有方法失败优雅降级：网络/解析失败返回 Empty / false / null，不向 UI 上抛异常。
    ///   4) 写入操作的签名（x-s / x-t）由签名提供器计算；沙箱内不可签名时降级失败但不抛。
    ///   5) 小红书无原生转发：RepostAsync 改为"生成分享卡片并写入系统剪贴板"。
    /// </summary>
    public sealed class XiaohongshuAdapter : IPlatformAdapter
    {
        private const string EdithBase = "https://edith.xiaohongshu.com";
        private const string WebBase = "https://www.xiaohongshu.com";

        private readonly AccountRepository _accounts;
        private readonly Func<AccountId, ResilientHttpClient> _clientFactory;
        private readonly IXhsSignatureProvider _signature;

        public XiaohongshuAdapter(AccountRepository accounts,
            Func<AccountId, ResilientHttpClient> clientFactory,
            IXhsSignatureProvider signature = null)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _signature = signature; // 可空：签名器未注入时写入操作将直接返回 false
        }

        public PlatformId Platform => PlatformId.Xiaohongshu;

        // ========== 凭证 ==========

        public bool ValidateCredential(AccountId account)
        {
            var cookie = GetCookieString(account);
            return !string.IsNullOrEmpty(cookie) && cookie.Contains("web_session=");
        }

        private string GetCookieString(AccountId account)
        {
            var bytes = _accounts.GetCredential(account);
            if (bytes == null || bytes.Length == 0) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return null; }
        }

        private static string ExtractCookieValue(string cookie, string name)
        {
            if (string.IsNullOrEmpty(cookie)) return null;
            var idx = cookie.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + name.Length + 1;
            var end = cookie.IndexOf(';', start);
            if (end < 0) end = cookie.Length;
            return cookie.Substring(start, end - start);
        }

        /// <summary>构造请求配置回调：Cookie + Referer + UA + 可选签名头。</summary>
        private Action<HttpRequestMessage> WithAuth(string cookie, string apiPath, string payload = null)
        {
            return req =>
            {
                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);
                req.Headers.TryAddWithoutValidation("Referer", WebBase + "/");
                req.Headers.TryAddWithoutValidation("Origin", WebBase);
                // 小红书 web 端常见 UA
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                // 写入操作需要 x-s / x-t 签名；签名器未注入则跳过，请求大概率失败由调用方降级
                if (_signature != null && !string.IsNullOrEmpty(apiPath))
                {
                    var sig = _signature.Sign(apiPath, payload ?? "", cookie);
                    if (sig != null)
                    {
                        req.Headers.TryAddWithoutValidation("x-s", sig.XS);
                        req.Headers.TryAddWithoutValidation("x-t", sig.XT);
                    }
                }
            };
        }

        // ========== 读取（ICrawlerAdapter） ==========

        public async Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            // 发现页：homefeed（cursor 翻页）
            var url = EdithBase + "/api/sns/web/v1/homefeed";
            var body = JsonSerializer.Serialize(new
            {
                cursor = cursor ?? "",
                cursor_enable = !string.IsNullOrEmpty(cursor),
                image_formats = new[] { "jpg", "webp", "png" },
                need_filter_image = false
            });
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/homefeed", body)).ConfigureAwait(false);
            return ParseFeed(json);
        }

        public async Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            // sort: popular|latest|image|video → keyword_type
            var sortNum = sort == "latest" ? 0 : sort == "image" ? 2 : sort == "video" ? 3 : 1;
            var url = EdithBase + "/api/sns/web/v1/search/notes";
            var body = JsonSerializer.Serialize(new
            {
                keyword = keyword,
                page = ParsePageFromCursor(cursor),
                page_size = 20,
                search_id = GenerateSearchId(),
                sort = sortNum,
                note_type = 0
            });
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/search/notes", body)).ConfigureAwait(false);
            return ParseSearch(json);
        }

        public async Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;

            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/feed?source_note_id=" + Uri.EscapeDataString(postId);
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie, "/api/sns/web/v1/feed")).ConfigureAwait(false);
            return ParseNoteDetail(json);
        }

        public async Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<Comment>.Empty;

            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v2/comment/page?note_id=" + Uri.EscapeDataString(postId)
                + "&cursor=" + (cursor ?? "")
                + "&image_formats=jpg,webp,png&top_comment_id=&xsec_token=" + ExtractCookieValue(cookie, "a1");
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie, "/api/sns/web/v2/comment/page")).ConfigureAwait(false);
            return ParseComments(json, postId);
        }

        public async Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;

            var client = _clientFactory(account);
            // self: /api/sns/web/v1/user/selfinfo；other: /api/sns/web/v1/user/othername?target_user=...
            var url = userId == "self" || userId == _accounts.Get(account)?.Uid
                ? EdithBase + "/api/sns/web/v1/user/selfinfo"
                : EdithBase + "/api/sns/web/v1/user/othername?target_uid=" + Uri.EscapeDataString(userId);
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie, "/api/sns/web/v1/user")).ConfigureAwait(false);
            return ParseUserProfile(json);
        }

        public async Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v2/note/collect/page?cursor=" + (cursor ?? "");
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie, "/api/sns/web/v2/note/collect/page")).ConfigureAwait(false);
            return ParseFeed(json);
        }

        public async Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            var health = new AccountHealth { CheckedAt = DateTimeOffset.UtcNow };
            if (string.IsNullOrEmpty(cookie))
            {
                health.IsLimited = true;
                health.RemainingQuota = 0;
                health.Level = "无凭证";
                return health;
            }

            // 调用 selfinfo 探测限流（参考 redbook health）
            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/user/selfinfo";
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie, "/api/sns/web/v1/user/selfinfo")).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                health.IsLimited = true;
                health.RemainingQuota = 0;
                health.Level = "请求失败";
            }
            else if (json.Contains("\"success\":true") || json.Contains("\"success\": true"))
            {
                health.IsLimited = false;
                health.RemainingQuota = 100;
                health.Level = "正常";
            }
            else
            {
                health.IsLimited = true;
                health.RemainingQuota = 10;
                health.Level = "疑似限流";
            }
            return health;
        }

        // ========== 写入（IInteractionAdapter） ==========

        public async Task<bool> LikeAsync(AccountId account, string postId, CancellationToken ct)
            => await NoteActionAsync(account, postId, "like", ct).ConfigureAwait(false);

        public async Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct)
            => await NoteActionAsync(account, postId, "dislike", ct).ConfigureAwait(false);

        private async Task<bool> NoteActionAsync(AccountId account, string postId, string op, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/note/" + op;
            var body = JsonSerializer.Serialize(new { note_oid = postId });
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/note/" + op, body)).ConfigureAwait(false);
            return ParseSuccess(json);
        }

        public async Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => await CollectActionAsync(account, postId, true, ct).ConfigureAwait(false);

        public async Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => await CollectActionAsync(account, postId, false, ct).ConfigureAwait(false);

        private async Task<bool> CollectActionAsync(AccountId account, string postId, bool collect, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/note/" + (collect ? "collect" : "dislike_favorite");
            var body = JsonSerializer.Serialize(new { note_oid = postId, note_id = postId });
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, url.Substring(EdithBase.Length), body)).ConfigureAwait(false);
            return ParseSuccess(json);
        }

        public async Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/user/follow";
            var body = JsonSerializer.Serialize(new { target_uid = userId, type = "follow" });
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/user/follow", body)).ConfigureAwait(false);
            return ParseSuccess(json);
        }

        public async Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;
            var client = _clientFactory(account);
            var url = EdithBase + "/api/sns/web/v1/comment/post";
            var bodyObj = new Dictionary<string, object>
            {
                ["note_id"] = postId,
                ["content"] = content,
                ["at_users"] = new List<object>()
            };
            if (!string.IsNullOrEmpty(replyToCommentId))
                bodyObj["target_comment_id"] = replyToCommentId;
            var body = JsonSerializer.Serialize(bodyObj);
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/comment/post", body)).ConfigureAwait(false);
            return ParseCreatedComment(json, postId, content);
        }

        public async Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;

            var client = _clientFactory(account);

            // 简化：仅发布图文笔记。真实流程需要先上传图片到私有 OSS 拿到 traceId，再 note/create
            // 这里实现"图片先单独上传，再调用 note/create"
            var imageInfos = new List<object>();
            if (request.ImagePaths != null && request.ImagePaths.Count > 0)
            {
                foreach (var path in request.ImagePaths)
                {
                    var imgId = await UploadImageAsync(client, cookie, path, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(imgId))
                        imageInfos.Add(new { file_id = imgId });
                }
            }

            var url = EdithBase + "/api/sns/web/v1/note/create";
            var bodyObj = new Dictionary<string, object>
            {
                ["note_type"] = imageInfos.Count > 0 ? 1 : 2, // 1=图文 2=纯文
                ["title"] = request.Text?.Length > 20 ? request.Text.Substring(0, 20) : request.Text ?? "",
                ["desc"] = request.Text ?? "",
                ["image_info"] = imageInfos,
                ["post_loc"] = new { type = "normal", info = request.Location ?? "" }
            };
            if (request.Tags != null && request.Tags.Count > 0)
            {
                bodyObj["tag_list"] = request.Tags;
            }
            var body = JsonSerializer.Serialize(bodyObj);
            var json = await client.PostJsonAsync(url, body, ct, WithAuth(cookie, "/api/sns/web/v1/note/create", body)).ConfigureAwait(false);
            return ParseCreatedNoteId(json);
        }

        private async Task<string> UploadImageAsync(ResilientHttpClient client, string cookie, string path, CancellationToken ct)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using var fs = System.IO.File.OpenRead(path);
                using var content = new MultipartFormDataContent
                {
                    { new StreamContent(fs), "file", System.IO.Path.GetFileName(path) }
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, EdithBase + "/api/media/v1/upload_file")
                {
                    Content = content
                };
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                req.Headers.TryAddWithoutValidation("Referer", WebBase + "/");
                using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var respStr = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(respStr);
                if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("file_id", out var fid))
                    return fid.ToString();
                return null;
            }
            catch { return null; }
        }

        public async Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct)
        {
            // 小红书无原生转发：生成分享卡片并写入系统剪贴板
            // 注意：await 缺失是有意为之——剪贴板写入是同步 Win32 调用，
            // 此处包装在 STA 子线程中执行后 Join 返回。await Task.Yield() 仅用于满足编译器警告。
            await System.Threading.Tasks.Task.Yield();
            try
            {
                var shareText = string.IsNullOrEmpty(comment)
                    ? $"我从小红书分享了一条笔记：{WebBase}/discovery/item/{postId}"
                    : $"{comment} | 分享自小红书：{WebBase}/discovery/item/{postId}";
                return WriteToClipboard(shareText);
            }
            catch { return false; }
        }

        /// <summary>
        /// 通过 Win32 API 写入剪贴板（CF_UNICODETEXT）。
        /// Adapter 层不引入 PresentationFramework / System.Windows.Forms，
        /// 直接 P/Invoke user32 + kernel32 实现，避免跨层依赖。
        /// 必须在 STA 线程上调用，因此用专用 STA 子线程包装。
        /// </summary>
        private static bool WriteToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var ok = false;
            try
            {
                var t = new System.Threading.Thread(() =>
                {
                    try
                    {
                        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return;
                        try
                        {
                            NativeMethods.EmptyClipboard();
                            // GMEM_MOVEABLE | GMEM_ZEROINIT = 0x0042
                            var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                            var hGlobal = NativeMethods.GlobalAlloc(0x0042, (uint)bytes.Length);
                            if (hGlobal == IntPtr.Zero) return;
                            var p = NativeMethods.GlobalLock(hGlobal);
                            if (p == IntPtr.Zero) return;
                            try
                            {
                                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, p, bytes.Length);
                            }
                            finally { NativeMethods.GlobalUnlock(hGlobal); }
                            // CF_UNICODETEXT = 13
                            var hData = NativeMethods.SetClipboardData(13, hGlobal);
                            ok = hData != IntPtr.Zero;
                            // SetClipboardData 成功后，所有权转移给系统；不要 GlobalFree(hGlobal)。
                        }
                        finally { NativeMethods.CloseClipboard(); }
                    }
                    catch { /* 静默忽略，调用方降级 */ }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
            }
            catch { /* ignore */ }
            return ok;
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool OpenClipboard(IntPtr hWndNewOwner);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool CloseClipboard();

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool EmptyClipboard();

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GlobalAlloc(uint uFlags, uint dwBytes);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr GlobalLock(IntPtr hMem);

            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GlobalUnlock(IntPtr hMem);
        }

        // ========== JSON 解析 ==========

        private static int ParsePageFromCursor(string cursor)
        {
            if (string.IsNullOrEmpty(cursor)) return 1;
            if (cursor.StartsWith("page=", StringComparison.OrdinalIgnoreCase)) cursor = cursor.Substring(5);
            return int.TryParse(cursor, out var n) ? Math.Max(1, n) : 1;
        }

        private static string GenerateSearchId()
        {
            // 模拟小红书搜索 ID（14 位时间戳 + 4 位随机）
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var rand = new Random().Next(1000, 9999).ToString();
            return ts.Substring(Math.Max(0, ts.Length - 10)) + rand;
        }

        private static PagedResult<PostCard> ParseFeed(string json)
        {
            if (string.IsNullOrEmpty(json)) return PagedResult<PostCard>.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return PagedResult<PostCard>.Empty;
                if (!data.TryGetProperty("items", out var items)) return PagedResult<PostCard>.Empty;
                var list = new List<PostCard>();
                foreach (var it in items.EnumerateArray())
                {
                    if (it.TryGetProperty("note_card", out var nc))
                        list.Add(MapNoteToCard(nc));
                    if (it.TryGetProperty("note", out var nb))
                        list.Add(MapNoteToCard(nb));
                }
                string next = null;
                if (data.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String)
                    next = c.GetString();
                return new PagedResult<PostCard>(list, next);
            }
            catch { return PagedResult<PostCard>.Empty; }
        }

        private static PagedResult<PostCard> ParseSearch(string json)
        {
            if (string.IsNullOrEmpty(json)) return PagedResult<PostCard>.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return PagedResult<PostCard>.Empty;
                if (!data.TryGetProperty("items", out var items)) return PagedResult<PostCard>.Empty;
                var list = new List<PostCard>();
                foreach (var it in items.EnumerateArray())
                {
                    // 兼容两种结构：直接带 note_card，或外层带 model_type 标识后再带 note_card
                    if (it.TryGetProperty("note_card", out var nc))
                        list.Add(MapNoteToCard(nc));
                    else if (it.TryGetProperty("id", out _))
                        list.Add(MapNoteToCard(it));
                }
                string next = null;
                if (data.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String)
                    next = c.GetString();
                if (data.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.False)
                    next = null;
                return new PagedResult<PostCard>(list, next);
            }
            catch { return PagedResult<PostCard>.Empty; }
        }

        private static PostDetail ParseNoteDetail(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("items", out var items)) return null;
                JsonElement? first = null;
                foreach (var it in items.EnumerateArray())
                    if (it.TryGetProperty("note", out var n)) { first = n; break; }
                if (first == null) return null;
                var note = first.Value;
                var card = MapNoteToCard(note);
                var detail = new PostDetail
                {
                    Id = card.Id,
                    FullText = TryGetText(note, "desc") ?? TryGetText(note, "title") ?? "",
                    Card = card
                };
                var imgs = new List<string>();
                if (note.TryGetProperty("image_list", out var imgsArr))
                    foreach (var img in imgsArr.EnumerateArray())
                        if (img.TryGetProperty("url_default", out var u) && u.ValueKind == JsonValueKind.String)
                            imgs.Add(u.GetString());
                // 视频流：note.video.media.stream.h264[0].master_url
                if (note.TryGetProperty("video", out var vid)
                    && vid.TryGetProperty("media", out var med)
                    && med.TryGetProperty("stream", out var stream)
                    && stream.TryGetProperty("h264", out var h264)
                    && h264.ValueKind == JsonValueKind.Array
                    && h264.GetArrayLength() > 0)
                {
                    var firstStream = h264[0];
                    if (firstStream.TryGetProperty("master_url", out var murl) && murl.ValueKind == JsonValueKind.String)
                        detail.VideoUrl = murl.GetString();
                }
                detail.ImageUrls = imgs;
                var tags = new List<string>();
                if (note.TryGetProperty("tag_list", out var tagArr))
                    foreach (var t in tagArr.EnumerateArray())
                        if (t.TryGetProperty("name", out var tn) && tn.ValueKind == JsonValueKind.String)
                            tags.Add(tn.GetString());
                detail.Tags = tags;
                return detail;
            }
            catch { return null; }
        }

        private static PagedResult<Comment> ParseComments(string json, string postId)
        {
            if (string.IsNullOrEmpty(json)) return PagedResult<Comment>.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return PagedResult<Comment>.Empty;
                if (!data.TryGetProperty("comments", out var comments)) return PagedResult<Comment>.Empty;
                var list = new List<Comment>();
                foreach (var c in comments.EnumerateArray())
                    list.Add(MapComment(c, postId));
                string next = null;
                if (data.TryGetProperty("cursor", out var cur) && cur.ValueKind == JsonValueKind.String)
                    next = cur.GetString();
                if (data.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.False)
                    next = null;
                return new PagedResult<Comment>(list, next);
            }
            catch { return PagedResult<Comment>.Empty; }
        }

        private static UserProfile ParseUserProfile(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("info", out var info) && !data.TryGetProperty("basic_info", out info))
                {
                    info = data;
                }
                return new UserProfile
                {
                    Id = TryGetText(info, "user_id") ?? TryGetText(info, "uid"),
                    Name = TryGetText(info, "nickname") ?? TryGetText(info, "name"),
                    Avatar = TryGetText(info, "image") ?? TryGetText(info, "avatar"),
                    Bio = TryGetText(info, "desc") ?? TryGetText(info, "description"),
                    FollowCount = info.TryGetProperty("follows", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 0,
                    FollowerCount = info.TryGetProperty("fans", out var fc) && fc.ValueKind == JsonValueKind.Number ? fc.GetInt32() : 0,
                    PostCount = info.TryGetProperty("interaction", out var it) && it.TryGetProperty("note_count", out var nc) && nc.ValueKind == JsonValueKind.Number ? nc.GetInt32() : 0
                };
            }
            catch { return null; }
        }

        private static bool ParseSuccess(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("success", out var s))
                    return s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.Number && s.GetInt32() == 1);
                if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
                    return c.GetInt32() == 0;
                return false;
            }
            catch { return false; }
        }

        private static Comment ParseCreatedComment(string json, string postId, string content)
        {
            if (string.IsNullOrEmpty(json) || !ParseSuccess(json)) return null;
            return new Comment
            {
                Id = "local-" + Guid.NewGuid().ToString("N"),
                PostId = postId,
                AuthorName = "我",
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static string ParseCreatedNoteId(string json)
        {
            if (string.IsNullOrEmpty(json) || !ParseSuccess(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("note_id", out var id))
                    return id.ToString();
                return "ok";
            }
            catch { return null; }
        }

        // ========== 模型映射 ==========

        private static PostCard MapNoteToCard(JsonElement note)
        {
            var card = new PostCard
            {
                Id = TryGetText(note, "note_id") ?? TryGetText(note, "id"),
                AuthorName = TryGetText(note, "user", "nickname") ?? TryGetText(note, "user", "name") ?? "未知用户",
                AuthorId = TryGetText(note, "user", "user_id") ?? TryGetText(note, "user", "uid"),
                AuthorAvatar = TryGetText(note, "user", "avatar"),
                CreatedAt = DateTimeOffset.UtcNow,
                TextSummary = TryGetText(note, "display_title") ?? TryGetText(note, "title") ?? TryGetText(note, "desc") ?? "",
                LikeCount = note.TryGetProperty("interact_info", out var ii) && ii.TryGetProperty("liked_count", out var lc) && lc.ValueKind == JsonValueKind.String && int.TryParse(lc.GetString(), out var liked) ? liked : 0,
                CommentCount = note.TryGetProperty("interact_info", out var ii2) && ii2.TryGetProperty("comment_count", out var cc) && cc.ValueKind == JsonValueKind.String && int.TryParse(cc.GetString(), out var com) ? com : 0,
                CollectCount = note.TryGetProperty("interact_info", out var ii3) && ii3.TryGetProperty("collected_count", out var colc) && colc.ValueKind == JsonValueKind.String && int.TryParse(colc.GetString(), out var col) ? col : 0
            };
            var type = TryGetText(note, "type");
            if (type == "video") { card.IsVideo = true; card.Media = MediaType.Video; }
            else if (note.TryGetProperty("image_list", out var _)) card.Media = MediaType.Images;
            else card.Media = MediaType.Text;
            return card;
        }

        private static Comment MapComment(JsonElement c, string postId)
        {
            return new Comment
            {
                Id = TryGetText(c, "id"),
                PostId = postId,
                AuthorName = TryGetText(c, "user_info", "nickname") ?? TryGetText(c, "user", "nickname") ?? "未知用户",
                AuthorId = TryGetText(c, "user_info", "user_id") ?? TryGetText(c, "user", "user_id"),
                Content = TryGetText(c, "content") ?? "",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(
                    c.TryGetProperty("create_time", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                LikeCount = c.TryGetProperty("like_count", out var lc) && lc.ValueKind == JsonValueKind.Number ? lc.GetInt32() : 0,
                ParentCommentId = c.TryGetProperty("target_comment_id", out var tci) ? (tci.ValueKind == JsonValueKind.String ? tci.GetString() : null) : null
            };
        }

        // ========== 文本工具 ==========

        private static string TryGetText(JsonElement el, params string[] path)
        {
            try
            {
                var cur = el;
                for (int i = 0; i < path.Length; i++)
                {
                    if (!cur.TryGetProperty(path[i], out var next)) return null;
                    cur = next;
                }
                if (cur.ValueKind == JsonValueKind.String) return cur.GetString();
                if (cur.ValueKind == JsonValueKind.Number) return cur.GetRawText();
                return null;
            }
            catch { return null; }
        }

        private static string TryGetText(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            }
            return null;
        }
    }

    /// <summary>
    /// 小红书签名提供器抽象。
    /// 实现方需要按 a1/web_id/x-s 算法（参考 Spider_XHS）计算签名；
    /// 沙箱/未注入时所有写入操作会优雅降级失败。
    /// </summary>
    public interface IXhsSignatureProvider
    {
        XhsSignature Sign(string apiPath, string payload, string cookie);
    }

    /// <summary>小红书请求签名。</summary>
    public sealed class XhsSignature
    {
        public string XS { get; set; }
        public string XT { get; set; }
    }

    /// <summary>
    /// 默认签名提供器（空实现）。
    /// 用于"未集成真实签名器"的运行环境：写入操作会失败，但读取接口仍能走 cookie-only 路径。
    /// 真实集成时注入带 a1/x-s 计算的实现。
    /// </summary>
    public sealed class NullXhsSignatureProvider : IXhsSignatureProvider
    {
        public XhsSignature Sign(string apiPath, string payload, string cookie) => null;
    }
}
