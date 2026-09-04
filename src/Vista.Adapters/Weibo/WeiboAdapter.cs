using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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

namespace Vista.Adapters.Weibo
{
    /// <summary>
    /// 微博适配器。基于 m.weibo.cn 移动版 API（参考 sinaweibopy 与 weibo_netcore_sdk）。
    /// 设计要点：
    ///   1) 每个 AccountId 独立 HttpClient + 限速桶（防关联，§五）。
    ///   2) 凭证 = WebView2 登录后从浏览器取出的 Cookie 字符串（UTF-8 字节）。
    ///   3) 所有方法失败优雅降级：网络/解析失败返回 Empty / false / null，不向 UI 上抛异常。
    ///   4) m.weibo.cn 不要求 OAuth2，只要 Cookie 中含 SUB 即可读取，XSRF-TOKEN 用于写入操作。
    /// </summary>
    public sealed class WeiboAdapter : IPlatformAdapter
    {
        private const string BaseUrl = "https://m.weibo.cn";

        private readonly AccountRepository _accounts;
        private readonly Func<AccountId, ResilientHttpClient> _clientFactory;

        public WeiboAdapter(AccountRepository accounts, Func<AccountId, ResilientHttpClient> clientFactory)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public PlatformId Platform => PlatformId.Weibo;

        // ========== 凭证 ==========

        public bool ValidateCredential(AccountId account)
        {
            var cookie = GetCookieString(account);
            return !string.IsNullOrEmpty(cookie) && cookie.Contains("SUB=");
        }

        /// <summary>从仓库取出 Cookie 字符串。byte[] 是 UTF-8 字节。</summary>
        private string GetCookieString(AccountId account)
        {
            var bytes = _accounts.GetCredential(account);
            if (bytes == null || bytes.Length == 0) return null;
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return null; }
        }

        /// <summary>从 Cookie 字符串中提取 XSRF-TOKEN 值。m.weibo.cn 写入操作需要。</summary>
        private static string ExtractXsrf(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) return null;
            var idx = cookie.IndexOf("XSRF-TOKEN=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + "XSRF-TOKEN=".Length;
            var end = cookie.IndexOf(';', start);
            if (end < 0) end = cookie.Length;
            var raw = cookie.Substring(start, end - start);
            // URL decode（XSRF-TOKEN 值中 %3D 等需要解码）
            return Uri.UnescapeDataString(raw);
        }

        /// <summary>构造带 Cookie + Referer + UA 的 GET 请求配置回调。</summary>
        private Action<HttpRequestMessage> WithAuth(string cookie, string referer = null)
        {
            return req =>
            {
                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);
                req.Headers.TryAddWithoutValidation("Referer", referer ?? BaseUrl + "/");
                req.Headers.TryAddWithoutValidation("MWeibo-Pwa", "1");
                if (cookie != null)
                {
                    var xsrf = ExtractXsrf(cookie);
                    if (!string.IsNullOrEmpty(xsrf))
                        req.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", xsrf);
                }
            };
        }

        // ========== 读取（ICrawlerAdapter） ==========

        public async Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            var since = string.IsNullOrEmpty(cursor) ? "" : "&since_id=" + Uri.EscapeDataString(cursor);
            var url = BaseUrl + "/api/feed/friends?for_video=0" + since;
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseTimeline(json);
        }

        public async Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            // sort: popular|latest|image|video → 微微博对应 containerid 后缀
            var sortSuffix = sort == "latest" ? "" : sort == "image" ? "%26filter_type%3Dimage" : sort == "video" ? "%26filter_type%3Dvideo" : "%26sort%3Dhot";
            // 微博搜索 containerid：100103type=1&q=keyword（最新）/ 100103type=61（热门）
            var containerid = "100103type%3D1%26q%3D" + Uri.EscapeDataString(keyword) + sortSuffix;
            var page = ParsePageFromCursor(cursor);
            var url = BaseUrl + "/api/container/getIndex?containerid=" + containerid + "&page_type=searchall&page=" + page;
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseSearch(json);
        }

        public async Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;

            var client = _clientFactory(account);
            var url = BaseUrl + "/statuses/show?id=" + Uri.EscapeDataString(postId);
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseStatusDetail(json);
        }

        public async Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<Comment>.Empty;

            var client = _clientFactory(account);
            // m.weibo.cn /comments/hotflow 用 max_id 游标翻页，不是 page：
            //   首页：?id=X&mid=X&max_id_type=0
            //   续页：?id=X&mid=X&max_id_type=0&max_id=YYY（YYY 来自上一次响应的 data.max_id）
            var maxIdParam = string.IsNullOrEmpty(cursor) ? "" : "&max_id=" + Uri.EscapeDataString(cursor);
            var url = BaseUrl + "/comments/hotflow?id=" + Uri.EscapeDataString(postId)
                + "&mid=" + Uri.EscapeDataString(postId)
                + "&max_id_type=0" + maxIdParam;
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseComments(json, postId);
        }

        public async Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;

            var client = _clientFactory(account);
            var url = BaseUrl + "/profile/info?uid=" + Uri.EscapeDataString(userId);
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseUserProfile(json);
        }

        public async Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return PagedResult<PostCard>.Empty;

            var client = _clientFactory(account);
            var page = ParsePageFromCursor(cursor);
            var url = BaseUrl + "/api/container/getIndex?containerid=2304401_-_favorite&page=" + page;
            var json = await client.GetStringAsync(url, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseSearch(json);
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

            var client = _clientFactory(account);
            // 调用一个轻量配置接口判断是否被风控
            var json = await client.GetStringAsync(BaseUrl + "/api/config", ct, WithAuth(cookie)).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                health.IsLimited = true;
                health.RemainingQuota = 0;
                health.Level = "请求失败";
            }
            else if (json.Contains("\"ok\":1") || json.Contains("\"ok\": 1"))
            {
                health.IsLimited = false;
                health.RemainingQuota = 100;
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
            => await AttitudesAsync(account, postId, "create", ct).ConfigureAwait(false);

        public async Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct)
            => await AttitudesAsync(account, postId, "destroy", ct).ConfigureAwait(false);

        private async Task<bool> AttitudesAsync(AccountId account, string postId, string op, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = BaseUrl + "/api/attitudes/" + op;
            var form = new[]
            {
                new KeyValuePair<string, string>("id", postId),
                new KeyValuePair<string, string>("attitude", "1"),
                new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? "")
            };
            var json = await client.PostFormAsync(url, form, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseOk(json);
        }

        public async Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => await StarredAsync(account, postId, true, ct).ConfigureAwait(false);

        public async Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => await StarredAsync(account, postId, false, ct).ConfigureAwait(false);

        private async Task<bool> StarredAsync(AccountId account, string postId, bool create, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = BaseUrl + "/api/starred/" + (create ? "create" : "destroy");
            var form = new[]
            {
                new KeyValuePair<string, string>("id", postId),
                new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? "")
            };
            var json = await client.PostFormAsync(url, form, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseOk(json);
        }

        public async Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct)
            => await FriendshipAsync(account, userId, "create", ct).ConfigureAwait(false);

        private async Task<bool> FriendshipAsync(AccountId account, string userId, string op, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = BaseUrl + "/api/friendships/" + op;
            var form = new[]
            {
                new KeyValuePair<string, string>("uid", userId),
                new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? "")
            };
            var json = await client.PostFormAsync(url, form, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseOk(json);
        }

        public async Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;
            var client = _clientFactory(account);
            var url = BaseUrl + "/api/comments/create";
            var formList = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("id", postId),
                new KeyValuePair<string, string>("content", content),
                new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? ""),
                new KeyValuePair<string, string>("mid", postId)
            };
            if (!string.IsNullOrEmpty(replyToCommentId))
                formList.Add(new KeyValuePair<string, string>("cid", replyToCommentId));
            var json = await client.PostFormAsync(url, formList, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseCreatedComment(json, postId, content);
        }

        public async Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return null;
            var client = _clientFactory(account);

            // 仅图片：上传 + statuses/upload；纯文本：statuses/update；视频：video/upload（M3 后完善）
            // 这里实现纯文本 + 九宫格图片上传
            if (request.ImagePaths != null && request.ImagePaths.Count > 0)
            {
                var picIds = new List<string>();
                foreach (var path in request.ImagePaths)
                {
                    var pid = await UploadPicAsync(client, cookie, path, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(pid)) picIds.Add(pid);
                }
                var form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("content", BuildPublishText(request)),
                    new KeyValuePair<string, string>("picId", string.Join(",", picIds)),
                    new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? "")
                };
                var json = await client.PostFormAsync(BaseUrl + "/api/statuses/upload", form, ct, WithAuth(cookie)).ConfigureAwait(false);
                return ParseCreatedStatusId(json);
            }
            else
            {
                var form = new[]
                {
                    new KeyValuePair<string, string>("content", BuildPublishText(request)),
                    new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? "")
                };
                var json = await client.PostFormAsync(BaseUrl + "/api/statuses/update", form, ct, WithAuth(cookie)).ConfigureAwait(false);
                return ParseCreatedStatusId(json);
            }
        }

        /// <summary>上传图片到 picupload 服务，返回 picId。失败返回 null。</summary>
        private async Task<string> UploadPicAsync(ResilientHttpClient client, string cookie, string path, CancellationToken ct)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                using var fs = System.IO.File.OpenRead(path);
                using var content = new MultipartFormDataContent
                {
                    { new StreamContent(fs), "pic", System.IO.Path.GetFileName(path) }
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://picupload.weibo.com/interface/pic_upload?ang=i&marks=1-1")
                {
                    Content = content
                };
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                req.Headers.TryAddWithoutValidation("Referer", "https://weibo.com");
                using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var respStr = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                // 解析 pid（响应为 JSONP-like，提取 "pid":"xxxx"）
                var pidIdx = respStr.IndexOf("\"pid\":\"", StringComparison.OrdinalIgnoreCase);
                if (pidIdx < 0) return null;
                var start = pidIdx + "\"pid\":\"".Length;
                var end = respStr.IndexOf('"', start);
                if (end < 0) return null;
                return respStr.Substring(start, end - start);
            }
            catch { return null; }
        }

        public async Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct)
        {
            var cookie = GetCookieString(account);
            if (string.IsNullOrEmpty(cookie)) return false;
            var client = _clientFactory(account);
            var url = BaseUrl + "/api/statuses/repost";
            var form = new[]
            {
                new KeyValuePair<string, string>("id", postId),
                new KeyValuePair<string, string>("content", comment ?? ""),
                new KeyValuePair<string, string>("st", ExtractXsrf(cookie) ?? ""),
                new KeyValuePair<string, string>("mid", postId)
            };
            var json = await client.PostFormAsync(url, form, ct, WithAuth(cookie)).ConfigureAwait(false);
            return ParseOk(json);
        }

        // ========== JSON 解析辅助 ==========

        /// <summary>把游标（形如 "page=2" 或数字字符串）解析为页码。无游标返回 1。</summary>
        private static int ParsePageFromCursor(string cursor)
        {
            if (string.IsNullOrEmpty(cursor)) return 1;
            if (cursor.StartsWith("page=", StringComparison.OrdinalIgnoreCase))
                cursor = cursor.Substring(5);
            return int.TryParse(cursor, out var n) ? Math.Max(1, n) : 1;
        }

        /// <summary>从下一页 page 构造游标。</summary>
        private static string CursorFromPage(int page) => "page=" + (page + 1);

        private static PagedResult<PostCard> ParseTimeline(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("\"statuses\"")) return PagedResult<PostCard>.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || ok.GetInt32() != 1) return PagedResult<PostCard>.Empty;
                if (!root.TryGetProperty("statuses", out var arr)) return PagedResult<PostCard>.Empty;
                var list = new List<PostCard>();
                foreach (var el in arr.EnumerateArray())
                    list.Add(MapStatusToCard(el));
                string next = null;
                if (root.TryGetProperty("since_id", out var sid) && sid.ValueKind == JsonValueKind.Number)
                    next = sid.GetInt64().ToString(CultureInfo.InvariantCulture);
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
                if (!root.TryGetProperty("ok", out var ok) || ok.GetInt32() != 1) return PagedResult<PostCard>.Empty;
                if (!root.TryGetProperty("data", out var data)) return PagedResult<PostCard>.Empty;
                if (!data.TryGetProperty("cards", out var cards)) return PagedResult<PostCard>.Empty;
                var list = new List<PostCard>();
                foreach (var card in cards.EnumerateArray())
                {
                    if (card.TryGetProperty("mblog", out var mb))
                        list.Add(MapStatusToCard(mb));
                    else if (card.TryGetProperty("card_group", out var group))
                        foreach (var g in group.EnumerateArray())
                            if (g.TryGetProperty("mblog", out var mb2))
                                list.Add(MapStatusToCard(mb2));
                }
                // 翻页：从 cardlistInfo.since 读取
                string next = null;
                if (data.TryGetProperty("cardlistInfo", out var info))
                {
                    if (info.TryGetProperty("page", out var p))
                        next = CursorFromPage(p.GetInt32());
                }
                return new PagedResult<PostCard>(list, next);
            }
            catch { return PagedResult<PostCard>.Empty; }
        }

        private static PostDetail ParseStatusDetail(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("id", out var id)) return null;
                var detail = new PostDetail
                {
                    Id = id.ToString(),
                    FullText = TryGetText(data, "text") ?? TryGetText(data, "raw_text") ?? "",
                    Card = MapStatusToCard(data)
                };
                var imgs = new List<string>();
                if (data.TryGetProperty("pic_ids", out var pics))
                    foreach (var p in pics.EnumerateArray())
                        imgs.Add("https://wx1.sinaimg.cn/large/" + p.GetString() + ".jpg");
                if (data.TryGetProperty("pic_infos", out var picInfos))
                    foreach (var prop in picInfos.EnumerateObject())
                        if (prop.Value.TryGetProperty("large", out var lg) && lg.TryGetProperty("url", out var url))
                            imgs.Add(url.GetString());
                detail.ImageUrls = imgs;
                if (data.TryGetProperty("tags", out var tags))
                {
                    var tagList = new List<string>();
                    foreach (var t in tags.EnumerateArray())
                        tagList.Add(t.GetString());
                    detail.Tags = tagList;
                }
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
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return PagedResult<Comment>.Empty;
                if (!data.TryGetProperty("data", out var arr)) return PagedResult<Comment>.Empty;
                var list = new List<Comment>();
                foreach (var c in arr.EnumerateArray())
                    list.Add(MapComment(c, postId));
                string next = null;
                if (data.TryGetProperty("max_id", out var mid) && mid.ValueKind == JsonValueKind.Number && mid.GetInt64() != 0)
                    next = mid.GetInt64().ToString(CultureInfo.InvariantCulture);
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
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return null;
                if (!data.TryGetProperty("user", out var user)) return null;
                return new UserProfile
                {
                    Id = TryGetText(user, "id"),
                    Name = TryGetText(user, "screen_name"),
                    Avatar = TryGetText(user, "avatar_hd") ?? TryGetText(user, "profile_image_url"),
                    Bio = TryGetText(user, "description"),
                    FollowCount = user.TryGetProperty("follow_count", out var fc) ? fc.GetInt32() : 0,
                    FollowerCount = user.TryGetProperty("followers_count", out var foc) ? foc.GetInt32() : 0,
                    PostCount = user.TryGetProperty("statuses_count", out var sc) ? sc.GetInt32() : 0
                };
            }
            catch { return null; }
        }

        private static bool ParseOk(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ok", out var ok))
                    return ok.ValueKind == JsonValueKind.Number ? ok.GetInt32() == 1 : ok.GetBoolean();
                return false;
            }
            catch { return false; }
        }

        private static Comment ParseCreatedComment(string json, string postId, string content)
        {
            if (string.IsNullOrEmpty(json) || !ParseOk(json)) return null;
            // 简化：直接根据用户输入构造一个 Comment 实体返回（真实接口返回结构复杂）
            return new Comment
            {
                Id = "local-" + Guid.NewGuid().ToString("N"),
                PostId = postId,
                AuthorName = "我",
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static string ParseCreatedStatusId(string json)
        {
            if (string.IsNullOrEmpty(json) || !ParseOk(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id))
                    return id.ToString();
                return "ok";
            }
            catch { return null; }
        }

        // ========== 模型映射 ==========

        private static PostCard MapStatusToCard(JsonElement el)
        {
            var card = new PostCard
            {
                Id = TryGetText(el, "id") ?? TryGetText(el, "mid"),
                AuthorName = TryGetText(el, "user", "screen_name") ?? "未知用户",
                AuthorId = TryGetText(el, "user", "id"),
                AuthorAvatar = TryGetText(el, "user", "avatar_hd") ?? TryGetText(el, "user", "profile_image_url"),
                CreatedAt = ParseWeiboTime(TryGetText(el, "created_at")),
                TextSummary = StripHtml(TryGetText(el, "text") ?? TryGetText(el, "raw_text") ?? ""),
                LikeCount = el.TryGetProperty("attitudes_count", out var like) ? like.GetInt32() : 0,
                CommentCount = el.TryGetProperty("comments_count", out var com) ? com.GetInt32() : 0,
                RepostCount = el.TryGetProperty("reposts_count", out var rp) ? rp.GetInt32() : 0,
                IsVideo = el.TryGetProperty("page_info", out var pg)
                          && pg.TryGetProperty("type", out var pt)
                          && pt.ValueKind == JsonValueKind.String
                          && pt.GetString() == "video"
            };
            if (el.TryGetProperty("pic_infos", out var pics) || el.TryGetProperty("pic_ids", out var pics2))
            {
                // 不需要从 pics/pics2 读出内容，只用于判断是否存在图片字段
                card.Media = card.IsVideo ? MediaType.Video : MediaType.Images;
            }
            else if (card.IsVideo) card.Media = MediaType.Video;
            else card.Media = MediaType.Text;
            return card;
        }

        private static Comment MapComment(JsonElement el, string postId)
        {
            return new Comment
            {
                Id = TryGetText(el, "id"),
                PostId = postId,
                AuthorName = TryGetText(el, "user", "screen_name") ?? "未知用户",
                AuthorId = TryGetText(el, "user", "id"),
                Content = StripHtml(TryGetText(el, "text") ?? ""),
                CreatedAt = ParseWeiboTime(TryGetText(el, "created_at")),
                LikeCount = el.TryGetProperty("like_count", out var lc) && lc.ValueKind == JsonValueKind.Number ? lc.GetInt32() : 0,
                ParentCommentId = TryGetText(el, "reply_id") == "0" ? null : TryGetText(el, "reply_id")
            };
        }

        private static string BuildPublishText(PublishRequest r)
        {
            var sb = new StringBuilder(r.Text ?? "");
            if (r.Tags != null && r.Tags.Count > 0)
                foreach (var t in r.Tags)
                    sb.Append(" #").Append(t).Append("#");
            if (!string.IsNullOrEmpty(r.Location))
                sb.Append(" ").Append(r.Location);
            return sb.ToString();
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
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return null;
        }

        /// <summary>去除 HTML 标签。微博 text 字段常含 &lt;a&gt; 等标签。</summary>
        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (var ch in s)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        /// <summary>解析微博时间格式：Sat Mar 24 12:00:00 +0800 2026。</summary>
        private static DateTimeOffset ParseWeiboTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTimeOffset.UtcNow;
            return DateTimeOffset.TryParseExact(s,
                "ddd MMM d HH:mm:ss zzz yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t : DateTimeOffset.UtcNow;
        }
    }
}
