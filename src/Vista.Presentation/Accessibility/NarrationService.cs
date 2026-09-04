using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using Vista.Core.Adapters.Models;

namespace Vista.Presentation.Accessibility
{
    /// <summary>
    /// 朗读服务。争渡读屏适配版：
    /// - 默认关闭「内置自动朗读」，程序不主动调 SAPI，避免与争渡"双声卡冲突、重复朗读"。
    /// - 朗读按钮/快捷键（Alt+Shift+R）属"用户主动动作"，始终执行（除非 EnableManualSpeak=false）。
    /// - 所有自动化场景（刷新完成、转发成功等）的状态提示：只写 UIA LiveRegion，让争渡按自己节奏读。
    /// </summary>
    /// <remarks>
    /// 重要：SpeechSynthesizer 不能在静态构造函数里 new——在某些 Windows（Server/LTSC/精简版/未装 TTS 语音）
    /// 上会抛 InvalidOperationException/PlatformNotSupportedException，被包装成 TypeInitializationException，
    /// 让整个 NarrationService 类型永久不可用，并连锁导致 App.OnStartup 静默崩溃（WPF dispatcher 还没起来，
    /// 用户看不到任何错误）。所以这里改成延迟创建：首次使用时尝试 new，失败就降级为 null，朗读功能禁用。
    /// </remarks>
    public static class NarrationService
    {
        private static SpeechSynthesizer _synth;
        private static bool _synthTried;     // 防止重复尝试初始化
        private static bool _synthAvailable = true;  // 初始假设可用；首次尝试失败后置 false
        private static readonly System.Threading.SemaphoreSlim _gate = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _isSpeaking;

        /// <summary>
        /// 延迟初始化 SpeechSynthesizer。SAPI 不可用时返回 null。
        /// 调用方：所有用 _synth 的地方都应改成 GetSynth()?.XXX。
        /// </summary>
        private static SpeechSynthesizer GetSynth()
        {
            if (_synth != null || _synthTried) return _synth;
            _synthTried = true;
            try
            {
                var synth = new SpeechSynthesizer();
                try { synth.SelectVoice("Microsoft Huihui Desktop"); } catch { /* 没装 Huihui，用默认语音 */ }
                synth.Rate = 0;
                _synth = synth;
            }
            catch (Exception)
            {
                // SAPI 5 / System.Speech 不可用（Server Core、未装 TTS、Speech Platform 缺失等）
                // 朗读功能整体降级为禁用，但不影响 App 启动。
                _synthAvailable = false;
                _synth = null;
            }
            return _synth;
        }

        // ========== 争渡适配：三大开关（可在设置里调整） ==========

        /// <summary>
        /// 内置自动朗读开关。false（默认）：程序自己不自动说话，所有状态提示交给争渡读屏（通过 UIA 属性）读取。
        /// 若用户未装读屏软件且希望 Vista 自朗读，可手动设为 true。
        /// </summary>
        public static bool EnableAutoSpeak { get; set; } = false;

        /// <summary>用户手动朗读（按钮/快捷键）开关。true（默认）：Alt+Shift+R / 朗读按钮 可用。</summary>
        public static bool EnableManualSpeak { get; set; } = true;

        /// <summary>检测到争渡读屏进程时自动配置（启动时调用）。</summary>
        public static bool IsZdsRunning { get; private set; }

        /// <summary>SAPI 是否可用（首次尝试 new SpeechSynthesizer 后才有意义）。</summary>
        public static bool IsSpeechAvailable => _synthAvailable;

        public static bool IsSpeaking => _isSpeaking;

        /// <summary>启动时自动检测争渡读屏，同步更新开关。</summary>
        public static void DetectReaderAndConfigure()
        {
            // 注意：此方法被 App.OnStartup 早期调用，绝不能在此触发 SpeechSynthesizer 的初始化
            // （SAPI 不可用会抛 TypeInitializationException 让整个 App 崩）。
            // 这里只做进程检测，朗读初始化留到首次实际需要时。
            var procs = new[] { "zds", "zdreader", "zdsx", "ZDSoft" };
            try
            {
                var running = Process.GetProcesses()
                    .Select(p => p.ProcessName)
                    .Any(name => procs.Contains(name, StringComparer.OrdinalIgnoreCase));
                IsZdsRunning = running;
            }
            catch { }

            if (IsZdsRunning)
            {
                // 发现争渡：关闭内置自动朗读，避免争渡 + SAPI 双声道
                EnableAutoSpeak = false;
            }
        }

        // ========== 对外 API ==========

        /// <summary>
        /// 用户手动朗读（按钮/快捷键）。始终执行（除非 EnableManualSpeak=false 或 SAPI 不可用）。
        /// 调用场景：朗读当前卡片、朗读评论列表、设置里点"试听声音"。
        /// </summary>
        public static void SpeakManual(string text)
        {
            if (!EnableManualSpeak || string.IsNullOrEmpty(text)) return;
            DoSpeak(text);
        }

        /// <summary>
        /// 自动朗读。仅在 EnableAutoSpeak=true 且未发现争渡读屏时生效。
        /// 调用场景：刷新完成提示、转发成功提示等——已改走 UIA LiveRegion，此方法基本保留给非读屏用户。
        /// </summary>
        public static void SpeakAuto(string text)
        {
            if (!EnableAutoSpeak) return;
            if (IsZdsRunning) return; // 争渡环境不自动说话
            if (string.IsNullOrEmpty(text)) return;
            DoSpeak(text);
        }

        private static void DoSpeak(string text)
        {
            var synth = GetSynth();
            if (synth == null) return; // SAPI 不可用，朗读降级禁用
            System.Threading.Tasks.Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _isSpeaking = true;
                    synth.Speak(text);
                }
                finally
                {
                    _isSpeaking = false;
                    _gate.Release();
                }
            });
        }

        public static void SpeakCommentsManual(IEnumerable<Comment> comments)
        {
            if (!EnableManualSpeak) return;
            var list = comments?.ToList();
            if (list == null || list.Count == 0)
            {
                DoSpeak("当前没有评论");
                return;
            }
            var parts = new List<string> { $"共 {list.Count} 条评论" };
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                parts.Add(c.SpokenLabel(i + 1));
            }
            DoSpeak(string.Join("；", parts));
        }

        public static void Stop()
        {
            var synth = GetSynth();
            try { synth?.SpeakAsyncCancelAll(); } catch { }
            _isSpeaking = false;
        }

        public static void SetRate(int rate)
        {
            var synth = GetSynth();
            if (synth == null) return;
            synth.Rate = rate < -10 ? -10 : (rate > 10 ? 10 : rate);
        }
    }
}
