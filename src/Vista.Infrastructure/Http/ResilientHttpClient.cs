using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace Vista.Infrastructure.Http
{
    /// <summary>
    /// 带"重试 + 熔断 + 限速 + 随机延迟"语义的 HttpClient 包装。
    /// 安全策略（用户要求 §安全策略）：
    ///   1) 所有请求间加入随机延迟 0.5~1.5 秒（模拟真人操作间隔，防机器人识别）。
    ///   2) 遇到限流错误（HTTP 429 / 5xx / 网络异常）采用指数退避重试：1s → 2s → 4s。
    ///   3) 4xx（除 429）直接返回，不重试——多为鉴权/参数错误，重试无意义且易触发风控。
    /// 每个账号拥有独立实例，实现"防关联"——独立 Client、独立 UA、独立设备指纹。
    /// </summary>
    public sealed class ResilientHttpClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retry;
        private static readonly Random _jitter = new Random();

        public ResilientHttpClient(HttpClient client, RateLimitBucket rateLimit)
        {
            _client = client;
            RateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
            // 指数退避：1s, 2s, 4s（最多 3 次）
            // 重试条件：HttpRequestException、5xx、429 Too Many Requests
            _retry = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r => (int)r.StatusCode >= 500 || (int)r.StatusCode == 429)
                .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i - 1)),
                    (outcome, delay, attempt, ctx) =>
                    {
                        // 真实日志由 Serilog 输出，此处保持静默以避免循环依赖
                    });
        }

        /// <summary>本 Client 的速率桶（按账号隔离）。</summary>
        public RateLimitBucket RateLimit { get; }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            await RateLimit.AcquireAsync(ct).ConfigureAwait(false);
            // 随机延迟 0.5~1.5 秒，模拟真人操作节奏，降低被风控识别为机器人的概率
            var delayMs = _jitter.Next(500, 1501);
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            return await _retry.ExecuteAsync(token => _client.SendAsync(req, token), ct).ConfigureAwait(false);
        }

        /// <summary>GET 便捷方法。失败时返回 null（不抛），由调用方决定降级策略。</summary>
        public async Task<string> GetStringAsync(string url, CancellationToken ct,
            Action<HttpRequestMessage> configure = null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                configure?.Invoke(req);
                using var resp = await SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch { return null; }
        }

        /// <summary>POST JSON 便捷方法。失败时返回 null（不抛）。</summary>
        public async Task<string> PostJsonAsync(string url, string jsonBody, CancellationToken ct,
            Action<HttpRequestMessage> configure = null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(jsonBody ?? "", System.Text.Encoding.UTF8, "application/json");
                configure?.Invoke(req);
                using var resp = await SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch { return null; }
        }

        /// <summary>POST 表单便捷方法。失败时返回 null（不抛）。</summary>
        public async Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form,
            CancellationToken ct, Action<HttpRequestMessage> configure = null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new FormUrlEncodedContent(form);
                configure?.Invoke(req);
                using var resp = await SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch { return null; }
        }

        public void Dispose() => _client?.Dispose();
    }
}
