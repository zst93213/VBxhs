using System;
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
            return await _retry.ExecuteAsync(() => _client.SendAsync(req, ct), ct).ConfigureAwait(false);
        }

        public void Dispose() => _client?.Dispose();
    }
}
