using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace ArgyleMigrator.Utils
{
    public static class Logger
    {
        private static ILogger _logger;

        public static void Initialize()
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "argyle-migrator-.log");
            
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Set as default logger for any static classes
            Log.Logger = _logger;
        }

        public static void Information(string messageTemplate, params object[] propertyValues)
        {
            _logger?.Information(messageTemplate, propertyValues);
        }

        public static void Warning(string messageTemplate, params object[] propertyValues)
        {
            _logger?.Warning(messageTemplate, propertyValues);
        }

        public static void Error(string messageTemplate, params object[] propertyValues)
        {
            _logger?.Error(messageTemplate, propertyValues);
        }

        public static void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            _logger?.Error(exception, messageTemplate, propertyValues);
        }

        public static void Debug(string messageTemplate, params object[] propertyValues)
        {
            _logger?.Debug(messageTemplate, propertyValues);
        }

        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
} 