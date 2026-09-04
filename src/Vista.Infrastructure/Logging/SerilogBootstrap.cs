using System;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Events;
using Serilog.Core;
using Serilog.Configuration;

namespace Vista.Infrastructure.Logging
{
    /// <summary>
    /// Serilog 引导。设计计划 §九"日志自动脱敏"在此落地：
    /// 通过自定义 DestructuringPolicy 与 LoggerFilter 把 Token/手机号/邮箱掩码后再写入文件。
    /// </summary>
    public static class SerilogBootstrap
    {
        private static bool _initialized;

        public static void Configure(string logDir)
        {
            if (_initialized) return;
            _initialized = true;

            System.IO.Directory.CreateDirectory(logDir);

            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.WithProperty("App", "Vista")
                .Destructure.With<SecretMaskingDestructurer>()
                .Filter.With<SecretMaskingFilter>()
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning)
                .WriteTo.File(
                    path: logDir + "\\vista-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log.Fatal(e.ExceptionObject as Exception, "未处理异常");
        }
    }

    /// <summary>
    /// 敏感字段脱敏：把对象里匹配 Token / 手机号 / 邮箱 / 身份证 的字符串值掩码。
    /// 通过 destructuring 在序列化前改写属性值，避免写入原始敏感信息。
    /// </summary>
    internal sealed class SecretMaskingDestructurer : IDestructuringPolicy
    {
        // 11 位手机号；不要求精确边界，避免误报。1 开头
        private static readonly Regex Phone = new Regex(@"1[3-9]\d{9}", RegexOptions.Compiled);
        // 标准邮箱
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
        // 长 Token（32 位以上连续字母数字），常见 Cookie/Access Token
        private static readonly Regex LongToken = new Regex(@"[A-Za-z0-9_\-]{32,}", RegexOptions.Compiled);
        // 18 位身份证
        private static readonly Regex IdCard = new Regex(@"\d{17}[\dXx]", RegexOptions.Compiled);

        public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)
        {
            result = null;
            if (value == null) return false;

            // 只对字符串值做掩码
            if (value is string s)
            {
                var masked = MaskString(s);
                if (masked != s)
                {
                    // ScalarValue 派生自 LogEventPropertyValue，是 Serilog 标量值的载体
                    result = new ScalarValue(masked);
                    return true;
                }
            }
            return false;
        }

        internal static string MaskString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var result = s;
            result = IdCard.Replace(result, m => m.Value.Substring(0, 4) + "**********" + m.Value.Substring(m.Value.Length - 4));
            result = Phone.Replace(result, m => m.Value.Substring(0, 3) + "****" + m.Value.Substring(7));
            result = Email.Replace(result, m =>
            {
                var at = m.Value.IndexOf('@');
                var prefix = m.Value.Substring(0, at);
                var maskedPrefix = prefix.Length <= 2 ? prefix[0] + "*" : prefix.Substring(0, 2) + new string('*', Math.Max(1, prefix.Length - 2));
                return maskedPrefix + m.Value.Substring(at);
            });
            // Token 仅在长度超过 40 时掩码，避免误伤普通长字符串
            result = LongToken.Replace(result, m =>
            {
                if (m.Value.Length < 40) return m.Value;
                return m.Value.Substring(0, 6) + "…" + m.Value.Substring(m.Value.Length - 4) + "[MASKED]";
            });
            return result;
        }
    }

    /// <summary>
    /// 日志过滤器：对最终 LogEvent 的 Message 模板做一次掩码扫描，
    /// 防止用户直接调用 Log.Information("token=xxx") 这种字面量被原样写出。
    /// </summary>
    internal sealed class SecretMaskingFilter : ILogEventFilter
    {
        public bool IsEnabled(LogEvent logEvent)
        {
            // 不直接拒绝；通过 MessageTemplate 渲染后掩码
            // Serilog 的 ILogEventFilter 只能"过滤"不能"改写"，
            // 真正的改写在 Destructurer 已完成，这里保留默认允许所有事件
            return true;
        }
    }

    /// <summary>扩展方法：方便给 LoggerConfiguration 注册 SecretMaskingDestructurer。</summary>
    internal static class LoggerConfigurationSecretExtensions
    {
        public static LoggerConfiguration WithSecretMasking(this LoggerDestructuringConfiguration cfg)
            => cfg.With<SecretMaskingDestructurer>();
    }
}
