using System;
using System.Threading.Tasks;
using Shared;
using Serilog;

namespace GMailThreadExtractor
{
    /// <summary>
    /// Provides retry logic with exponential backoff for handling transient failures.
    /// </summary>
    public static class RetryHelper
    {
        /// <summary>
        /// Executes an async operation with retry logic and exponential backoff.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="maxAttempts">Maximum number of retry attempts (default: 3).</param>
        /// <param name="baseDelay">Base delay between retries (default: 1 second).</param>
        /// <param name="maxDelay">Maximum delay between retries (default: 30 seconds).</param>
        /// <param name="operationName">Name of the operation for logging purposes.</param>
        /// <returns>The result of the successful operation.</returns>
        /// <exception cref="Exception">Throws the final exception from the operation when retries are exhausted.</exception>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int maxAttempts = 3,
            TimeSpan? baseDelay = null,
            TimeSpan? maxDelay = null,
            string operationName = "operation")
        {
            baseDelay ??= TimeSpan.FromSeconds(1);
            maxDelay ??= TimeSpan.FromSeconds(30);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    LoggingConfiguration.Logger.Debug("Attempting {OperationName} (attempt {Attempt}/{MaxAttempts})", operationName, attempt, maxAttempts);
                    return await operation();
                }
                catch (Exception ex)
                {
                    var isRetryable = IsRetryableException(ex);
                    var finalAttempt = attempt >= maxAttempts;

                    if (isRetryable && !finalAttempt)
                    {
                        // Calculate exponential backoff delay: baseDelay * 2^(attempt-1)
                        var delay = TimeSpan.FromMilliseconds(
                            Math.Min(
                                baseDelay.Value.TotalMilliseconds * (1 << (attempt - 1)),
                                maxDelay.Value.TotalMilliseconds));

                        LoggingConfiguration.Logger.Warning("{OperationName} failed (attempt {Attempt}/{MaxAttempts}): {ErrorMessage}", operationName, attempt, maxAttempts, ex.Message);
                        LoggingConfiguration.Logger.Information("Retrying in {DelaySeconds:F1} seconds...", delay.TotalSeconds);

                        await Task.Delay(delay);
                        continue;
                    }

                    // Non-retryable exception or final attempt: log and surface original exception
                    LoggingConfiguration.Logger.Error("{OperationName} failed permanently: {ErrorMessage}", operationName, ex.Message);
                    throw;
                }
            }

            // This should never be reached, but just in case
            throw new InvalidOperationException($"Operation '{operationName}' exhausted retries without returning a result.");
        }

        /// <summary>
        /// Executes an async operation with retry logic (void return).
        /// </summary>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="maxAttempts">Maximum number of retry attempts (default: 3).</param>
        /// <param name="baseDelay">Base delay between retries (default: 1 second).</param>
        /// <param name="maxDelay">Maximum delay between retries (default: 30 seconds).</param>
        /// <param name="operationName">Name of the operation for logging purposes.</param>
        /// <exception cref="Exception">Throws the final exception from the operation when retries are exhausted.</exception>
        public static async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            int maxAttempts = 3,
            TimeSpan? baseDelay = null,
            TimeSpan? maxDelay = null,
            string operationName = "operation")
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            }, maxAttempts, baseDelay, maxDelay, operationName);
        }

        /// <summary>
        /// Determines if an exception is retryable (network/connection issues).
        /// </summary>
        /// <param name="exception">The exception to check.</param>
        /// <returns>True if the exception indicates a transient failure that can be retried.</returns>
        private static bool IsRetryableException(Exception exception)
        {
            // Check for common retryable exceptions
            return exception switch
            {
                // Network connectivity issues
                System.Net.Sockets.SocketException => true,
                System.Net.NetworkInformation.NetworkInformationException => true,
                HttpRequestException => true,

                // Timeout exceptions
                TimeoutException => true,
                TaskCanceledException => true,
                OperationCanceledException => true,

                // IMAP/MailKit specific retryable exceptions
                MailKit.Net.Imap.ImapProtocolException => true,
                MailKit.CommandException cmd when IsRetryableCommandException(cmd) => true,
                MailKit.ServiceNotConnectedException => true,
                MailKit.ServiceNotAuthenticatedException => false, // Don't retry auth failures

                // IO exceptions (disk full, network drive disconnected, etc.)
                IOException io when IsRetryableIOException(io) => true,

                // Aggregate exceptions - check inner exceptions
                AggregateException agg => agg.InnerExceptions.Any(IsRetryableException),

                // Default: don't retry unknown exceptions
                _ => false
            };
        }

        /// <summary>
        /// Determines if a MailKit CommandException is retryable.
        /// </summary>
        private static bool IsRetryableCommandException(MailKit.CommandException exception)
        {
            // Retry on temporary server errors, but not on permanent failures
            var message = exception.Message.ToLowerInvariant();

            // Temporary failures (5xx response codes in IMAP context)
            if (message.Contains("temporary") ||
                message.Contains("try again") ||
                message.Contains("server busy") ||
                message.Contains("overloaded"))
            {
                return true;
            }

            // Permanent failures - don't retry
            if (message.Contains("authentication") ||
                message.Contains("credentials") ||
                message.Contains("permission") ||
                message.Contains("not found") ||
                message.Contains("invalid"))
            {
                return false;
            }

            // Default to not retrying unknown command exceptions
            return false;
        }

        /// <summary>
        /// Determines if an IOException is retryable.
        /// </summary>
        private static bool IsRetryableIOException(IOException exception)
        {
            var message = exception.Message.ToLowerInvariant();

            // Retryable IO issues
            return message.Contains("network") ||
                   message.Contains("connection") ||
                   message.Contains("timeout") ||
                   message.Contains("temporary");
        }
    }
}
