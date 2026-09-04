using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public static class NarrationService
    {
        private static readonly SpeechSynthesizer _synth;
        private static readonly System.Threading.SemaphoreSlim _gate = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _isSpeaking;

        static NarrationService()
        {
            _synth = new SpeechSynthesizer();
            try { _synth.SelectVoice("Microsoft Huihui Desktop"); } catch { }
            _synth.Rate = 0;
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

        public static bool IsSpeaking => _isSpeaking;

        /// <summary>启动时自动检测争渡读屏，同步更新开关。</summary>
        public static void DetectReaderAndConfigure()
        {
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
        /// 用户手动朗读（按钮/快捷键）。始终执行（除非 EnableManualSpeak=false）。
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
            System.Threading.Tasks.Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _isSpeaking = true;
                    _synth.Speak(text);
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
            _synth.SpeakAsyncCancelAll();
            _isSpeaking = false;
        }

        public static void SetRate(int rate) => _synth.Rate = rate < -10 ? -10 : (rate > 10 ? 10 : rate);
    }
}
