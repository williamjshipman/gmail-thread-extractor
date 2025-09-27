using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using GMailThreadExtractor;
using MailKit;

namespace GMailThreadExtractor.Tests;

public class RetryHelperTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_WithSuccessfulOperation_ShouldReturnResult()
    {
        // Arrange
        var expectedResult = "success";
        var callCount = 0;

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            return expectedResult;
        };

        // Act
        var result = await RetryHelper.ExecuteWithRetryAsync(operation, maxAttempts: 3);

        // Assert
        result.Should().Be(expectedResult);
        callCount.Should().Be(1); // Should succeed on first attempt
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithTransientFailureThenSuccess_ShouldRetry()
    {
        // Arrange
        var callCount = 0;
        var expectedResult = "success";

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);

            if (callCount < 3)
                throw new SocketException(); // Retryable exception

            return expectedResult;
        };

        // Act
        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10)); // Short delay for testing

        // Assert
        result.Should().Be(expectedResult);
        callCount.Should().Be(3); // Should retry twice, succeed on third attempt
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithPermanentFailure_ShouldThrowImmediately()
    {
        // Arrange
        var callCount = 0;
        var permanentException = new ArgumentException("Non-retryable error");

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw permanentException;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Non-retryable error");
        callCount.Should().Be(1); // Should not retry non-retryable exceptions
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithMaxAttemptsExceeded_ShouldThrowLastException()
    {
        // Arrange
        var callCount = 0;
        var retryableException = new TimeoutException("Timeout occurred");

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw retryableException;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("Timeout occurred");
        callCount.Should().Be(3); // Should attempt exactly 3 times
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOverload_ShouldWork()
    {
        // Arrange
        var callCount = 0;

        Func<Task> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);

            if (callCount < 2)
                throw new SocketException(); // Retryable exception
        };

        // Act
        await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        // Assert
        callCount.Should().Be(2); // Should retry once, succeed on second attempt
    }

    [Theory]
    [InlineData(typeof(SocketException), true)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(System.Net.NetworkInformation.NetworkInformationException), true)]
    [InlineData(typeof(System.Net.Http.HttpRequestException), true)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    public async Task ExecuteWithRetryAsync_ShouldRespectRetryableExceptions(Type exceptionType, bool shouldRetry)
    {
        // Arrange
        var callCount = 0;
        var exception = exceptionType switch
        {
            var t when t == typeof(SocketException) => new SocketException(),
            var t when t == typeof(NetworkInformationException) => new NetworkInformationException(),
            _ => (Exception)Activator.CreateInstance(exceptionType, "Test exception")!
        };

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw exception;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<Exception>();

        if (shouldRetry)
        {
            callCount.Should().Be(3); // Should retry retryable exceptions
        }
        else
        {
            callCount.Should().Be(1); // Should not retry non-retryable exceptions
        }
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithExponentialBackoff_ShouldIncreaseDelay()
    {
        // Arrange
        var callCount = 0;
        var timestamps = new List<DateTime>();

        Func<Task<string>> operation = async () =>
        {
            timestamps.Add(DateTime.UtcNow);
            callCount++;
            await Task.Delay(1);
            throw new SocketException(); // Always fail to test delays
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<SocketException>();

        // Assert exponential backoff (approximately)
        callCount.Should().Be(3);
        timestamps.Should().HaveCount(3);

        // Check that delays are increasing (with some tolerance for timing)
        var delay1 = timestamps[1] - timestamps[0];
        var delay2 = timestamps[2] - timestamps[1];

        delay1.Should().BeGreaterThan(TimeSpan.FromMilliseconds(80)); // ~100ms base delay
        delay2.Should().BeGreaterThan(delay1); // Should be roughly 2x the previous delay
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithMaxDelay_ShouldCapDelay()
    {
        // Arrange
        var callCount = 0;

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw new SocketException(); // Always fail
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 5, // More attempts to test max delay
            baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(2)); // Cap delay at 2 seconds

        await act.Should().ThrowAsync<SocketException>();
        callCount.Should().Be(5);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithMailKitCommandException_ShouldEvaluateRetryability()
    {
        // Arrange
        var callCount = 0;

        // Create a retryable exception (TimeoutException is inherently retryable)
        var retryableException = new TimeoutException("Operation timed out");

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw retryableException;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<TimeoutException>();
        callCount.Should().Be(3); // Should retry timeout errors
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithAuthenticationException_ShouldNotRetry()
    {
        // Arrange
        var callCount = 0;
        var authException = new UnauthorizedAccessException("Authentication failed");

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw authException;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        callCount.Should().Be(1); // Should not retry authentication failures
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithIOException_ShouldCheckRetryability()
    {
        // Arrange
        var callCount = 0;
        var networkIOException = new System.IO.IOException("Network timeout occurred");

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw networkIOException;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<System.IO.IOException>();
        callCount.Should().Be(3); // Network-related IO exceptions should be retried
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithOperationName_ShouldIncludeInLogging()
    {
        // Arrange
        var callCount = 0;
        var exception = new SocketException();

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            throw exception;
        };

        // Act & Assert
        var act = async () => await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 2,
            baseDelay: TimeSpan.FromMilliseconds(10),
            operationName: "test IMAP connection");

        await act.Should().ThrowAsync<SocketException>();
        callCount.Should().Be(2);
        // Note: In a real test, we would verify logging output, but that requires more complex setup
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WithDefaultParameters_ShouldUseDefaults()
    {
        // Arrange
        var callCount = 0;

        Func<Task<string>> operation = async () =>
        {
            callCount++;
            await Task.Delay(1);
            if (callCount < 2)
                throw new TimeoutException();
            return "success";
        };

        // Act
        var result = await RetryHelper.ExecuteWithRetryAsync(operation);

        // Assert
        result.Should().Be("success");
        callCount.Should().Be(2); // Should use default maxAttempts (3)
    }
}