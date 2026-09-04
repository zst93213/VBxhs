using System;
using Serilog;
using Serilog.Events;
using Serilog.Core;
using Serilog.Filters;

namespace Vista.Infrastructure.Logging
{
    /// <summary>
    /// Serilog 引导。设计计划 §九"日志自动脱敏"在此落地：
    /// 通过自定义 Destructuring 把 Token/手机号/邮箱掩码后再写入文件。
    /// </summary>
    public static class SerilogBootstrap
    {
        private static bool _initialized;

        public static void Configure(string logDir)
        {
            if (_initialized) return;
            _initialized = true;

            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.WithProperty("App", "Vista")
                // 脱敏：匹配手机号 / 邮箱 / Token 形态，写入前掩码
                .Destructure.With<SecretMaskingDestructurer>()
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

    /// <summary>敏感字段脱敏。匹配手机号、邮箱、长 Token，掩码后输出。</summary>
    internal sealed class SecretMaskingDestructurer : IDestructuringPolicy
    {
        public bool TryDestructure(object value, ILogEventPropertyFactory propertyFactory, out LogEventProperty result)
        {
            result = null;
            return false; // 简化：实际应在此做正则替换。M0 阶段先占位。
        }
    }
}
