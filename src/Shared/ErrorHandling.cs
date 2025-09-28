using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace Shared
{
    /// <summary>
    /// Defines categories of errors for consistent handling across the application.
    /// </summary>
    public enum ErrorCategory
    {
        /// <summary>Configuration or user input errors</summary>
        Configuration,
        /// <summary>Network connectivity or IMAP protocol errors</summary>
        Network,
        /// <summary>File system or I/O related errors</summary>
        FileSystem,
        /// <summary>Authentication or permission errors</summary>
        Authentication,
        /// <summary>Email processing or format errors</summary>
        EmailProcessing,
        /// <summary>Compression or archive errors</summary>
        Compression,
        /// <summary>Unexpected system or application errors</summary>
        System
    }

    /// <summary>
    /// Defines how errors should be handled.
    /// </summary>
    public enum ErrorHandlingStrategy
    {
        /// <summary>Log the error but continue processing</summary>
        LogAndContinue,
        /// <summary>Log the error and skip the current item</summary>
        LogAndSkip,
        /// <summary>Log the error and throw an exception</summary>
        LogAndThrow,
        /// <summary>Log the error and terminate the operation</summary>
        LogAndTerminate
    }

    /// <summary>
    /// Represents a structured error with category and context information.
    /// </summary>
    public class StructuredError
    {
        public ErrorCategory Category { get; }
        public string Message { get; }
        public Exception? InnerException { get; }
        public string? Context { get; }
        public DateTime Timestamp { get; }

        public StructuredError(ErrorCategory category, string message, Exception? innerException = null, string? context = null)
        {
            Category = category;
            Message = message;
            InnerException = innerException;
            Context = context;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Provides standardized error handling across the application.
    /// </summary>
    public static class ErrorHandler
    {
        private static readonly List<StructuredError> _errors = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Gets all errors that have been logged during the current session.
        /// </summary>
        public static IReadOnlyList<StructuredError> Errors
        {
            get
            {
                lock (_lock)
                {
                    return _errors.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Handles an error according to the standardized patterns.
        /// </summary>
        /// <param name="category">The category of the error.</param>
        /// <param name="message">A descriptive error message.</param>
        /// <param name="exception">The underlying exception, if any.</param>
        /// <param name="context">Additional context about where/when the error occurred.</param>
        /// <param name="strategy">How the error should be handled.</param>
        /// <returns>True if processing should continue, false if it should stop.</returns>
        public static bool Handle(ErrorCategory category, string message, Exception? exception = null, string? context = null, ErrorHandlingStrategy? strategy = null)
        {
            // Determine strategy if not specified
            strategy ??= GetDefaultStrategy(category, exception);

            var error = new StructuredError(category, message, exception, context);

            // Log the error
            LogError(error, strategy.Value);

            // Record the error
            lock (_lock)
            {
                _errors.Add(error);
            }

            // Execute the strategy
            return ExecuteStrategy(strategy.Value, error);
        }

        /// <summary>
        /// Handles an exception using automatic categorization.
        /// </summary>
        /// <param name="exception">The exception to handle.</param>
        /// <param name="context">Additional context about where the error occurred.</param>
        /// <param name="strategy">How the error should be handled (auto-determined if null).</param>
        /// <returns>True if processing should continue, false if it should stop.</returns>
        public static bool HandleException(Exception exception, string? context = null, ErrorHandlingStrategy? strategy = null)
        {
            var category = CategorizeException(exception);
            var message = $"{category} error: {exception.Message}";
            return Handle(category, message, exception, context, strategy);
        }

        /// <summary>
        /// Clears all recorded errors (useful for testing or starting fresh operations).
        /// </summary>
        public static void ClearErrors()
        {
            lock (_lock)
            {
                _errors.Clear();
            }
        }

        /// <summary>
        /// Gets a summary of all errors by category.
        /// </summary>
        public static Dictionary<ErrorCategory, int> GetErrorSummary()
        {
            var summary = new Dictionary<ErrorCategory, int>();

            lock (_lock)
            {
                foreach (var error in _errors)
                {
                    summary[error.Category] = summary.GetValueOrDefault(error.Category) + 1;
                }
            }

            return summary;
        }

        private static ErrorHandlingStrategy GetDefaultStrategy(ErrorCategory category, Exception? exception)
        {
            return category switch
            {
                ErrorCategory.Configuration => ErrorHandlingStrategy.LogAndThrow,
                ErrorCategory.Authentication => ErrorHandlingStrategy.LogAndThrow,
                ErrorCategory.Network => ErrorHandlingStrategy.LogAndThrow, // RetryHelper handles retries
                ErrorCategory.FileSystem => IsRecoverableFileSystemError(exception) ? ErrorHandlingStrategy.LogAndSkip : ErrorHandlingStrategy.LogAndThrow,
                ErrorCategory.EmailProcessing => ErrorHandlingStrategy.LogAndSkip, // Skip bad emails, continue with others
                ErrorCategory.Compression => ErrorHandlingStrategy.LogAndThrow,
                ErrorCategory.System => ErrorHandlingStrategy.LogAndThrow,
                _ => ErrorHandlingStrategy.LogAndThrow
            };
        }

        private static bool IsRecoverableFileSystemError(Exception? exception)
        {
            return exception switch
            {
                UnauthorizedAccessException => false, // Can't recover from permission issues
                DirectoryNotFoundException => false, // Missing directories are configuration issues
                IOException io when io.Message.Contains("being used by another process") => true, // Temporary file locks
                IOException io when io.Message.Contains("disk full") => false, // Can't recover from disk full
                _ => false
            };
        }

        private static ErrorCategory CategorizeException(Exception exception)
        {
            return exception switch
            {
                // Configuration errors (most specific first)
                ArgumentNullException => ErrorCategory.Configuration,
                ArgumentException => ErrorCategory.Configuration,
                InvalidOperationException => ErrorCategory.Configuration,

                // Authentication errors
                UnauthorizedAccessException => ErrorCategory.Authentication,

                // Network/IMAP errors
                System.Net.Sockets.SocketException => ErrorCategory.Network,
                System.Net.NetworkInformation.NetworkInformationException => ErrorCategory.Network,
                HttpRequestException => ErrorCategory.Network,
                TimeoutException => ErrorCategory.Network,

                // File system errors (most specific first)
                DirectoryNotFoundException => ErrorCategory.FileSystem,
                FileNotFoundException => ErrorCategory.FileSystem,
                IOException => ErrorCategory.FileSystem,

                // Email processing errors
                MimeKit.ParseException => ErrorCategory.EmailProcessing,
                FormatException when exception.Message.Contains("email") => ErrorCategory.EmailProcessing,

                // Compression errors
                InvalidDataException => ErrorCategory.Compression,

                // Aggregate exceptions - categorize by first inner exception
                AggregateException agg when agg.InnerExceptions.Count > 0 => CategorizeException(agg.InnerExceptions[0]),

                // System errors
                OutOfMemoryException => ErrorCategory.System,
                StackOverflowException => ErrorCategory.System,

                // MailKit exceptions (by type name to avoid inheritance conflicts)
                _ when exception.GetType().Name.Contains("ServiceNotAuthenticatedException") => ErrorCategory.Authentication,
                _ when exception.GetType().Name.Contains("AuthenticationException") => ErrorCategory.Authentication,
                _ when exception.GetType().Name.Contains("ImapProtocolException") => ErrorCategory.Network,
                _ when exception.GetType().Name.Contains("ServiceNotConnectedException") => ErrorCategory.Network,
                _ when exception.GetType().Name.Contains("CommandException") => ErrorCategory.Network,
                _ when exception.GetType().Name.Contains("ParseException") => ErrorCategory.EmailProcessing,

                // Default
                _ => ErrorCategory.System
            };
        }

        private static void LogError(StructuredError error, ErrorHandlingStrategy strategy)
        {
            var logger = LoggingConfiguration.Logger;

            var logLevel = strategy switch
            {
                ErrorHandlingStrategy.LogAndContinue => Serilog.Events.LogEventLevel.Warning,
                ErrorHandlingStrategy.LogAndSkip => Serilog.Events.LogEventLevel.Warning,
                ErrorHandlingStrategy.LogAndThrow => Serilog.Events.LogEventLevel.Error,
                ErrorHandlingStrategy.LogAndTerminate => Serilog.Events.LogEventLevel.Fatal,
                _ => Serilog.Events.LogEventLevel.Error
            };

            logger.Write(logLevel, error.InnerException,
                "{ErrorCategory} error: {ErrorMessage} {Context}",
                error.Category,
                error.Message,
                error.Context ?? "");
        }

        private static bool ExecuteStrategy(ErrorHandlingStrategy strategy, StructuredError error)
        {
            return strategy switch
            {
                ErrorHandlingStrategy.LogAndContinue => true,
                ErrorHandlingStrategy.LogAndSkip => true,
                ErrorHandlingStrategy.LogAndThrow => throw new ApplicationException(error.Message, error.InnerException),
                ErrorHandlingStrategy.LogAndTerminate => false,
                _ => throw new ApplicationException(error.Message, error.InnerException)
            };
        }
    }
}