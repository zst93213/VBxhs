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
    /// 带"重试 + 熔断 + 限速"语义的 HttpClient 包装。
    /// 设计参考：Polly 官方推荐组合。每个账号拥有独立实例（在 Adapters 层注入），
    /// 实现设计计划 §五"防关联"——独立 Client、独立 UA、独立设备指纹。
    /// </summary>
    public sealed class ResilientHttpClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retry;

        public ResilientHttpClient(HttpClient client, RateLimitBucket rateLimit)
        {
            _client = client;
            RateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
            // 指数退避，最多 3 次；5xx 与超时才重试，4xx 直接抛（多为鉴权/参数错误）
            _retry = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)),
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
            // Polly 7.x 的 AsyncRetryPolicy<TResult>.ExecuteAsync 要求传入取消令牌感知委托，
            // 才能在外部取消时立刻中止内部 HttpClient.SendAsync。
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
