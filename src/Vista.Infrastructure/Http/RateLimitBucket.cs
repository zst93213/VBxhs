using System.Threading;
using System.Threading.Tasks;

namespace Vista.Infrastructure.Http
{
    /// <summary>
    /// 令牌桶限速器。按账号隔离，多账号并行互不影响（设计计划 §五）。
    /// 设计计划 §三"适配层 · 速率限制"与 §五"多账号并行"的具体落地。
    /// </summary>
    public sealed class RateLimitBucket
    {
        private readonly int _capacity;        // 桶容量
        private readonly double _refillPerSec;  // 每秒回填令牌数
        private double _tokens;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public RateLimitBucket(int capacity, double refillPerSec)
        {
            _capacity = capacity;
            _refillPerSec = refillPerSec;
            _tokens = capacity;
        }

        /// <summary>取一个令牌，不够则阻塞等待回填。</summary>
        public async Task AcquireAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                while (_tokens < 1)
                {
                    var need = 1 - _tokens;
                    var waitMs = (int)(need / _refillPerSec * 1000);
                    if (waitMs < 10) waitMs = 10;
                    _gate.Release();
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                    await _gate.WaitAsync(ct).ConfigureAwait(false);
                    Refill();
                }
                _tokens -= 1;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Refill()
        {
            // 简化版：每次进入时按容量上限回填。真实场景可记录上次回填时间精确计算。
            if (_tokens < _capacity) _tokens = System.Math.Min(_capacity, _tokens + _refillPerSec);
        }
    }
}
