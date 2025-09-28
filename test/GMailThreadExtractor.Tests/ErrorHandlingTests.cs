using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using Shared;

namespace GMailThreadExtractor.Tests;

public class ErrorHandlingTests
{
    [Fact]
    public void Handle_WithNetworkError_ShouldCategorizeCorrectly()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var socketException = new SocketException();

        // Act & Assert
        var act = () => ErrorHandler.HandleException(socketException, "Test network operation");
        act.Should().Throw<ApplicationException>(); // Network errors should throw by default

        // Verify the error was logged
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.Network);
        ErrorHandler.Errors[0].InnerException.Should().Be(socketException);
        ErrorHandler.Errors[0].Context.Should().Be("Test network operation");
    }

    [Fact]
    public void Handle_WithFileSystemError_ShouldCategorizeCorrectly()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var ioException = new IOException("File not found");

        // Act & Assert
        var act = () => ErrorHandler.HandleException(ioException, "File operation");
        act.Should().Throw<ApplicationException>(); // Non-recoverable file errors should throw by default

        // Verify the error was logged
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.FileSystem);
    }

    [Fact]
    public void Handle_WithConfigurationError_ShouldCategorizeCorrectly()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var argException = new ArgumentException("Invalid configuration");

        // Act & Assert
        var act = () => ErrorHandler.HandleException(argException);
        act.Should().Throw<ApplicationException>(); // Configuration errors should throw by default

        // Verify the error was logged
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.Configuration);
    }

    [Fact]
    public void Handle_WithEmailProcessingError_ShouldAllowContinuation()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var formatException = new FormatException("Invalid email format");

        // Act
        var shouldContinue = ErrorHandler.Handle(
            ErrorCategory.EmailProcessing,
            "Failed to parse email",
            formatException,
            strategy: ErrorHandlingStrategy.LogAndSkip);

        // Assert
        shouldContinue.Should().BeTrue(); // Email processing errors can be skipped
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.EmailProcessing);
    }

    [Theory]
    [InlineData(typeof(SocketException), ErrorCategory.Network)]
    [InlineData(typeof(HttpRequestException), ErrorCategory.Network)]
    [InlineData(typeof(TimeoutException), ErrorCategory.Network)]
    [InlineData(typeof(IOException), ErrorCategory.FileSystem)]
    [InlineData(typeof(DirectoryNotFoundException), ErrorCategory.FileSystem)]
    [InlineData(typeof(UnauthorizedAccessException), ErrorCategory.Authentication)]
    [InlineData(typeof(ArgumentException), ErrorCategory.Configuration)]
    [InlineData(typeof(InvalidDataException), ErrorCategory.Compression)]
    [InlineData(typeof(OutOfMemoryException), ErrorCategory.System)]
    public void CategorizeException_ShouldReturnCorrectCategory(Type exceptionType, ErrorCategory expectedCategory)
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var exception = exceptionType == typeof(SocketException)
            ? new SocketException()
            : (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        ErrorHandler.HandleException(exception, strategy: ErrorHandlingStrategy.LogAndContinue);

        // Assert
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(expectedCategory);
    }

    [Fact]
    public void Handle_WithLogAndContinueStrategy_ShouldReturnTrue()
    {
        // Arrange
        ErrorHandler.ClearErrors();

        // Act
        var shouldContinue = ErrorHandler.Handle(
            ErrorCategory.System,
            "Test error",
            strategy: ErrorHandlingStrategy.LogAndContinue);

        // Assert
        shouldContinue.Should().BeTrue();
        ErrorHandler.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Handle_WithLogAndSkipStrategy_ShouldReturnTrue()
    {
        // Arrange
        ErrorHandler.ClearErrors();

        // Act
        var shouldContinue = ErrorHandler.Handle(
            ErrorCategory.EmailProcessing,
            "Malformed email",
            strategy: ErrorHandlingStrategy.LogAndSkip);

        // Assert
        shouldContinue.Should().BeTrue();
        ErrorHandler.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Handle_WithLogAndThrowStrategy_ShouldThrow()
    {
        // Arrange
        ErrorHandler.ClearErrors();

        // Act & Assert
        var act = () => ErrorHandler.Handle(
            ErrorCategory.Configuration,
            "Invalid configuration",
            strategy: ErrorHandlingStrategy.LogAndThrow);

        act.Should().Throw<ApplicationException>().WithMessage("Invalid configuration");
        ErrorHandler.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Handle_WithLogAndTerminateStrategy_ShouldReturnFalse()
    {
        // Arrange
        ErrorHandler.ClearErrors();

        // Act
        var shouldContinue = ErrorHandler.Handle(
            ErrorCategory.System,
            "Critical system error",
            strategy: ErrorHandlingStrategy.LogAndTerminate);

        // Assert
        shouldContinue.Should().BeFalse();
        ErrorHandler.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void GetErrorSummary_WithMultipleErrors_ShouldReturnCorrectCounts()
    {
        // Arrange
        ErrorHandler.ClearErrors();

        ErrorHandler.Handle(ErrorCategory.Network, "Network error 1", strategy: ErrorHandlingStrategy.LogAndContinue);
        ErrorHandler.Handle(ErrorCategory.Network, "Network error 2", strategy: ErrorHandlingStrategy.LogAndContinue);
        ErrorHandler.Handle(ErrorCategory.Configuration, "Config error", strategy: ErrorHandlingStrategy.LogAndContinue);
        ErrorHandler.Handle(ErrorCategory.EmailProcessing, "Email error", strategy: ErrorHandlingStrategy.LogAndContinue);

        // Act
        var summary = ErrorHandler.GetErrorSummary();

        // Assert
        summary.Should().HaveCount(3);
        summary[ErrorCategory.Network].Should().Be(2);
        summary[ErrorCategory.Configuration].Should().Be(1);
        summary[ErrorCategory.EmailProcessing].Should().Be(1);
    }

    [Fact]
    public void ClearErrors_ShouldRemoveAllErrors()
    {
        // Arrange - Clear any existing errors from other tests first
        ErrorHandler.ClearErrors();
        ErrorHandler.Handle(ErrorCategory.System, "Test error", strategy: ErrorHandlingStrategy.LogAndContinue);
        ErrorHandler.Errors.Should().HaveCount(1);

        // Act
        ErrorHandler.ClearErrors();

        // Assert
        ErrorHandler.Errors.Should().BeEmpty();
        ErrorHandler.GetErrorSummary().Should().BeEmpty();
    }

    [Fact]
    public void StructuredError_ShouldCaptureTimestamp()
    {
        // Arrange
        var beforeTime = DateTime.UtcNow;

        // Act
        var error = new StructuredError(ErrorCategory.System, "Test error");

        // Assert
        var afterTime = DateTime.UtcNow;
        error.Timestamp.Should().BeOnOrAfter(beforeTime);
        error.Timestamp.Should().BeOnOrBefore(afterTime);
        error.Category.Should().Be(ErrorCategory.System);
        error.Message.Should().Be("Test error");
        error.InnerException.Should().BeNull();
        error.Context.Should().BeNull();
    }

    [Fact]
    public void StructuredError_WithFullData_ShouldCaptureAllFields()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");
        var context = "Operation context";

        // Act
        var error = new StructuredError(ErrorCategory.Authentication, "Auth failed", innerException, context);

        // Assert
        error.Category.Should().Be(ErrorCategory.Authentication);
        error.Message.Should().Be("Auth failed");
        error.InnerException.Should().Be(innerException);
        error.Context.Should().Be(context);
    }

    [Fact]
    public void Handle_WithRecoverableFileSystemError_ShouldUseLogAndSkip()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var recoverableIO = new IOException("The process cannot access the file because it is being used by another process");

        // Act
        var shouldContinue = ErrorHandler.HandleException(recoverableIO);

        // Assert
        shouldContinue.Should().BeTrue(); // Recoverable file errors should be skipped
        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.FileSystem);
    }

    [Fact]
    public void Handle_WithNonRecoverableFileSystemError_ShouldThrow()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var nonRecoverableIO = new UnauthorizedAccessException("Access denied");

        // Act & Assert
        var act = () => ErrorHandler.HandleException(nonRecoverableIO);
        act.Should().Throw<ApplicationException>();

        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.Authentication);
    }

    [Fact]
    public void Handle_WithAggregateException_ShouldCheckInnerExceptions()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var socketException = new SocketException();
        var aggregateException = new AggregateException(socketException);

        // Act & Assert
        var act = () => ErrorHandler.HandleException(aggregateException);
        act.Should().Throw<ApplicationException>(); // Should categorize as network and throw

        ErrorHandler.Errors.Should().HaveCount(1);
        ErrorHandler.Errors[0].Category.Should().Be(ErrorCategory.Network);
    }

    [Fact]
    public async Task Handle_ThreadSafety_ShouldHandleConcurrentAccess()
    {
        // Arrange
        ErrorHandler.ClearErrors();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                ErrorHandler.Handle(
                    ErrorCategory.System,
                    $"Concurrent error {index}",
                    strategy: ErrorHandlingStrategy.LogAndContinue);
            }));
        }

        await Task.WhenAll(tasks.ToArray());

        // Assert
        ErrorHandler.Errors.Should().HaveCount(10);
        ErrorHandler.GetErrorSummary()[ErrorCategory.System].Should().Be(10);
    }
}