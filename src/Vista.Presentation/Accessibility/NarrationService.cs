using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using Vista.Core.Adapters.Models;

namespace Vista.Presentation.Accessibility
{
    public static class NarrationService
    {
        private static readonly SpeechSynthesizer _synth;
        private static readonly System.Threading.SemaphoreSlim _gate = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _isSpeaking;

        static NarrationService()
        {
            _synth = new SpeechSynthesizer();
            try { _synth.SelectVoice("Microsoft Huihui Desktop"); }
            catch { }
            _synth.Rate = 0;
        }

        public static bool IsSpeaking => _isSpeaking;

        public static void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
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

        public static void SpeakAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            System.Threading.Tasks.Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _isSpeaking = true;
                    _synth.SpeakAsync(text);
                    _synth.SpeakCompleted += (s, e) =>
                    {
                        _isSpeaking = false;
                        _gate.Release();
                    };
                }
                catch
                {
                    _isSpeaking = false;
                    _gate.Release();
                }
            });
        }

        public static void SpeakComments(IEnumerable<Comment> comments)
        {
            var list = comments?.ToList();
            if (list == null || list.Count == 0)
            {
                Speak("当前没有评论");
                return;
            }

            var summary = $"共 {list.Count} 条评论。";
            var parts = new List<string> { summary };
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                var time = c.CreatedAt != default ? c.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm") : "";
                var line = $"{i + 1}楼，{c.AuthorName} {time} 说：{c.Content}";
                if (c.LikeCount > 0) line += $"，{c.LikeCount} 赞";
                if (!string.IsNullOrEmpty(c.ParentCommentId)) line += "（回复）";
                parts.Add(line);
            }
            Speak(string.Join("；", parts));
        }

        public static void Stop()
        {
            _synth.SpeakAsyncCancelAll();
            _isSpeaking = false;
        }

        public static void SetRate(int rate) => _synth.Rate = Math.Clamp(rate, -10, 10);
    }
}
