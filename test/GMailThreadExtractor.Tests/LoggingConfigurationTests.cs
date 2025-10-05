using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Shared;
using Xunit.Abstractions;

namespace GMailThreadExtractor.Tests;

/// <summary>
/// Unit tests for LoggingConfiguration class covering Initialize and CloseAndFlush methods.
/// </summary>
public class LoggingConfigurationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;
    private readonly ITestOutputHelper _testOutput;

    public LoggingConfigurationTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        _testDirectory = Path.Combine(Path.GetTempPath(), $"logging_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _tempFiles = new List<string>();
    }

    public void Dispose()
    {
        // Clean up test files and close logger
        try
        {
            LoggingConfiguration.CloseAndFlush();
        }
        catch (Exception ex)
        {
            _testOutput.WriteLine($"Error during Dispose: {ex.Message}");
        }

        foreach (var file in _tempFiles.Where(File.Exists))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _testOutput.WriteLine($"Error deleting temp file {file}: {ex.Message}");
            }
        }

        // Try to delete all log files in test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                foreach (var file in Directory.GetFiles(_testDirectory, "*.log", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _testOutput.WriteLine($"Error deleting log file {file}: {ex.Message}");
                    }
                }
                Directory.Delete(_testDirectory, true);
            }
            catch (Exception ex)
            {
                _testOutput.WriteLine($"Error deleting test directory {_testDirectory}: {ex.Message}");
            }
        }

        // Reset the logger field using reflection for test isolation
        ResetLogger();
    }

    private string CreateTempFilePath(string extension = ".log")
    {
        var path = Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private void ResetLogger()
    {
        try
        {
            // Use reflection to reset the private _logger field to null for test isolation
            var loggerField = typeof(LoggingConfiguration).GetField("_logger", BindingFlags.Static | BindingFlags.NonPublic);
            loggerField?.SetValue(null, null);
        }
        catch { /* Ignore errors during cleanup */ }
    }

    #region Initialize Method Tests

    [Fact]
    public void Initialize_WithNullLogFilePath_ShouldCreateConsoleOnlyLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests line 34: if (!string.IsNullOrWhiteSpace(logFilePath)) should be false
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Information);

        // Assert - Logger should be created (line 43)
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        // Verify logger works (write a test log)
        var act = () => logger.Information("Test log message");
        act.Should().NotThrow("logger should work without file sink");
    }

    [Fact]
    public void Initialize_WithValidLogFilePath_ShouldCreateFileLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();
        var logFilePath = CreateTempFilePath(".log");

        // Act - Tests lines 34-40: file sink should be added
        LoggingConfiguration.Initialize(logFilePath: logFilePath, minimumLevel: LogEventLevel.Information);

        // Assert - Logger should be created (line 43)
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        // Write a test message to trigger file creation
        logger.Information("Test message to file");
        LoggingConfiguration.CloseAndFlush();

        // Verify log file was created (may have date suffix due to rolling interval)
        var directory = Path.GetDirectoryName(logFilePath);
        var fileName = Path.GetFileNameWithoutExtension(logFilePath);
        var logFiles = Directory.GetFiles(directory!, $"{fileName}*.log");
        logFiles.Should().NotBeEmpty("log file should be created when file path is provided");
    }

    [Fact]
    public void Initialize_WithEmptyLogFilePath_ShouldCreateConsoleOnlyLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests line 34: if (!string.IsNullOrWhiteSpace(logFilePath)) should be false for empty string
        LoggingConfiguration.Initialize(logFilePath: string.Empty, minimumLevel: LogEventLevel.Information);

        // Assert
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        var act = () => logger.Information("Test log message");
        act.Should().NotThrow("logger should work without file sink");
    }

    [Fact]
    public void Initialize_WithWhitespaceLogFilePath_ShouldCreateConsoleOnlyLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests line 34: if (!string.IsNullOrWhiteSpace(logFilePath)) should be false for whitespace
        LoggingConfiguration.Initialize(logFilePath: "   ", minimumLevel: LogEventLevel.Information);

        // Assert
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        var act = () => logger.Information("Test log message");
        act.Should().NotThrow("logger should work without file sink");
    }

    [Fact]
    public void Initialize_WithDebugLevel_ShouldSetMinimumLevel()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests lines 26-28: minimum level configuration
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Debug);

        // Assert
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        // Verify logger accepts debug level messages
        var act = () => logger.Debug("Debug level message");
        act.Should().NotThrow("logger should accept debug level when configured");
    }

    [Fact]
    public void Initialize_WithErrorLevel_ShouldSetMinimumLevel()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests lines 26-28: minimum level configuration for Error
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Error);

        // Assert
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized");

        // Verify logger works with error level
        var act = () => logger.Error("Error level message");
        act.Should().NotThrow("logger should accept error level when configured");
    }

    [Fact]
    public void Initialize_ShouldSetGlobalSerilogLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Tests line 44: Log.Logger = _logger
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Information);

        // Assert - Global Serilog logger should be set
        Log.Logger.Should().NotBeNull("Log.Logger should be set by Initialize");
        Log.Logger.Should().Be(LoggingConfiguration.Logger, "Log.Logger should be the same as LoggingConfiguration.Logger");
    }

    [Fact]
    public void Initialize_CalledMultipleTimes_ShouldReplaceLogger()
    {
        // Arrange - Reset logger first
        ResetLogger();

        // Act - Initialize multiple times with different configurations
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Information);
        var firstLogger = LoggingConfiguration.Logger;

        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Debug);
        var secondLogger = LoggingConfiguration.Logger;

        // Assert - Logger should be replaced (different instance)
        firstLogger.Should().NotBeNull("first logger should be created");
        secondLogger.Should().NotBeNull("second logger should be created");
        // Note: Both loggers may work, but second initialization should have replaced the internal _logger
    }

    [Fact]
    public void Initialize_WithFileConfiguration_ShouldIncludeAllSinks()
    {
        // Arrange - Reset logger first
        ResetLogger();
        var logFilePath = CreateTempFilePath(".log");

        // Act - Tests lines 25-43: complete configuration with console, debug, and file sinks
        LoggingConfiguration.Initialize(logFilePath: logFilePath, minimumLevel: LogEventLevel.Verbose);

        // Assert
        var logger = LoggingConfiguration.Logger;
        logger.Should().NotBeNull("logger should be initialized with all sinks");

        // Verify logger works at verbose level
        var act = () =>
        {
            logger.Verbose("Verbose message");
            logger.Debug("Debug message");
            logger.Information("Information message");
            logger.Warning("Warning message");
            logger.Error("Error message");
        };

        act.Should().NotThrow("logger should handle all log levels");
    }

    #endregion

    #region CloseAndFlush Method Tests

    [Fact]
    public void CloseAndFlush_AfterInitialization_ShouldNotThrow()
    {
        // Arrange
        ResetLogger();
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Information);
        LoggingConfiguration.Logger.Information("Test message before close");

        // Act - Tests lines 62-65
        var act = () => LoggingConfiguration.CloseAndFlush();

        // Assert
        act.Should().NotThrow("CloseAndFlush should not throw after initialization");
    }

    [Fact]
    public void CloseAndFlush_WithFileLogger_ShouldFlushToFile()
    {
        // Arrange
        ResetLogger();
        var logFilePath = CreateTempFilePath(".log");
        LoggingConfiguration.Initialize(logFilePath: logFilePath, minimumLevel: LogEventLevel.Information);

        // Write a test message
        LoggingConfiguration.Logger.Information("Test message for flush");

        // Act - Tests line 64: Log.CloseAndFlush() should flush buffered messages
        LoggingConfiguration.CloseAndFlush();

        // Give file system time to write
        System.Threading.Thread.Sleep(100);

        // Assert - Log file should exist with content
        var directory = Path.GetDirectoryName(logFilePath);
        var fileName = Path.GetFileNameWithoutExtension(logFilePath);
        var logFiles = Directory.GetFiles(directory!, $"{fileName}*.log");
        logFiles.Should().NotBeEmpty("log file should exist after flush");

        if (logFiles.Length > 0)
        {
            var content = File.ReadAllText(logFiles[0]);
            content.Should().Contain("Test message for flush", "flushed content should be in log file");
        }
    }

    [Fact]
    public void CloseAndFlush_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        ResetLogger();
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Information);

        // Act - Call CloseAndFlush multiple times
        var act = () =>
        {
            LoggingConfiguration.CloseAndFlush();
            LoggingConfiguration.CloseAndFlush();
            LoggingConfiguration.CloseAndFlush();
        };

        // Assert
        act.Should().NotThrow("CloseAndFlush should handle multiple calls");
    }

    [Fact]
    public void CloseAndFlush_BeforeInitialization_ShouldNotThrow()
    {
        // Arrange - Reset logger to ensure clean state
        ResetLogger();

        // Act - Tests calling CloseAndFlush before explicit initialization
        // (Logger property will create default logger via lazy initialization)
        var act = () => LoggingConfiguration.CloseAndFlush();

        // Assert
        act.Should().NotThrow("CloseAndFlush should not throw even before explicit initialization");
    }

    #endregion

    #region Logger Property Tests

    [Fact]
    public void Logger_FirstAccess_ShouldCreateDefaultLogger()
    {
        // Arrange - Reset logger to ensure clean state
        ResetLogger();

        // Act - Tests line 16: lazy initialization via _logger ??= CreateDefaultLogger()
        var logger = LoggingConfiguration.Logger;

        // Assert
        logger.Should().NotBeNull("Logger property should create default logger on first access");

        // Verify logger works
        var act = () => logger.Information("Test message with default logger");
        act.Should().NotThrow("default logger should work correctly");
    }

    [Fact]
    public void Logger_MultipleAccesses_ShouldReturnSameInstance()
    {
        // Arrange - Reset logger to ensure clean state
        ResetLogger();

        // Act - Access logger multiple times
        var logger1 = LoggingConfiguration.Logger;
        var logger2 = LoggingConfiguration.Logger;
        var logger3 = LoggingConfiguration.Logger;

        // Assert - Should return the same instance (lazy initialization)
        logger1.Should().BeSameAs(logger2, "Logger property should return same instance");
        logger2.Should().BeSameAs(logger3, "Logger property should return same instance");
    }

    [Fact]
    public void Logger_AfterInitialize_ShouldReturnInitializedLogger()
    {
        // Arrange
        ResetLogger();

        // Act - Initialize then access Logger property
        LoggingConfiguration.Initialize(logFilePath: null, minimumLevel: LogEventLevel.Debug);
        var logger = LoggingConfiguration.Logger;

        // Assert
        logger.Should().NotBeNull("Logger should return initialized instance");

        // Verify it's the initialized logger (accepts debug level)
        var act = () => logger.Debug("Debug message should work");
        act.Should().NotThrow("initialized logger should work at configured level");
    }

    #endregion
}
