using Serilog;
using Serilog.Events;

namespace Shared
{
    /// <summary>
    /// Provides centralized logging configuration for the application.
    /// </summary>
    public static class LoggingConfiguration
    {
        private static ILogger? _logger;

        /// <summary>
        /// Gets the global logger instance.
        /// </summary>
        public static ILogger Logger => _logger ??= CreateDefaultLogger();

        /// <summary>
        /// Initializes logging with the specified configuration.
        /// </summary>
        /// <param name="logFilePath">Path for the log file. If null, only console logging is used.</param>
        /// <param name="minimumLevel">Minimum log level to capture.</param>
        public static void Initialize(string? logFilePath = null, LogEventLevel minimumLevel = LogEventLevel.Information)
        {
            var config = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.Debug();

            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                config = config.WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
            }

            _logger = config.CreateLogger();
            Log.Logger = _logger;
        }

        /// <summary>
        /// Creates a default logger configuration for fallback scenarios.
        /// </summary>
        private static ILogger CreateDefaultLogger()
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        /// <summary>
        /// Closes and flushes the logger.
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
}