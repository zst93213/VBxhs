using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vista.Infrastructure.Http
{
    /// <summary>
    /// 写操作限流器（按账号隔离）。
    /// 安全策略要求：
    ///   - 评论、发博等写入操作间隔不低于 10 秒/次
    ///   - 每小时不超过 60 次写入
    /// 读取操作（信息流、搜索等）不走此限流器，仅走 RateLimitBucket 的总频率限制。
    /// </summary>
    public sealed class WriteRateLimiter
    {
        private readonly TimeSpan _minInterval;
        private readonly int _maxPerHour;
        private readonly TimeSpan _hourWindow;
        private readonly Queue<DateTimeOffset> _timestamps;
        private DateTimeOffset _lastWrite;
        private readonly SemaphoreSlim _gate;

        public WriteRateLimiter(TimeSpan minInterval, int maxPerHour)
        {
            _minInterval = minInterval;
            _maxPerHour = maxPerHour;
            _hourWindow = TimeSpan.FromHours(1);
            _timestamps = new Queue<DateTimeOffset>();
            _lastWrite = DateTimeOffset.MinValue;
            _gate = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// 等待直到满足写入条件：
        ///   1) 距上次写入至少 minInterval
        ///   2) 过去一小时内写入次数 < maxPerHour
        /// 若小时额度已满，抛 InvalidOperationException，由调用方决定是否稍后重试。
        /// </summary>
        public async Task AcquireAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 清理超过 1 小时的时间戳
                var cutoff = DateTimeOffset.UtcNow - _hourWindow;
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= _maxPerHour)
                    throw new InvalidOperationException(
                        $"写入频率超限：过去 1 小时已写入 {_timestamps.Count} 次（上限 {_maxPerHour}），请稍后再试。");

                // 距上次写入不足 minInterval 则等待
                var elapsed = DateTimeOffset.UtcNow - _lastWrite;
                if (elapsed < _minInterval)
                {
                    var wait = _minInterval - elapsed;
                    _gate.Release();
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    await _gate.WaitAsync(ct).ConfigureAwait(false);
                }

                _lastWrite = DateTimeOffset.UtcNow;
                _timestamps.Enqueue(_lastWrite);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
