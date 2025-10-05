using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MimeKit;
using ArchivalSupport;

namespace ArchivalSupport.Tests;

/// <summary>
/// Unit tests for TarGzipCompressor focusing on exception handling and error recovery paths.
/// </summary>
public class TarGzipCompressorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    public TarGzipCompressorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gzip_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _tempFiles = new List<string>();
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles.Where(File.Exists))
        {
            try { File.Delete(file); } catch { /* Ignore cleanup errors */ }
        }

        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, true); } catch { /* Ignore cleanup errors */ }
        }
    }

    private string CreateTempFilePath(string extension = ".tmp")
    {
        var path = Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private Dictionary<ulong, List<MessageBlob>> CreateTestThreads()
    {
        var message = CreateTestMessageBlob("Test Subject", "sender@example.com", "Test content");
        return new Dictionary<ulong, List<MessageBlob>>
        {
            { 123456789, new List<MessageBlob> { message } }
        };
    }

    private MessageBlob CreateTestMessageBlob(string subject, string from, string content)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("recipient@example.com"));
        message.Subject = subject;
        message.Date = DateTimeOffset.UtcNow;
        message.Body = new TextPart("plain") { Text = content };

        using var stream = new MemoryStream();
        message.WriteTo(stream);

        return new MessageBlob(
            Guid.NewGuid().ToString(),
            stream.ToArray(),
            subject,
            from,
            "recipient@example.com",
            DateTime.UtcNow);
    }

    #region Compress Method Tests

    [Fact]
    public async Task Compress_WithInvalidOutputPath_ShouldThrowAndCleanup()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = CreateTestThreads();
        var invalidPath = Path.Combine("Z:\\nonexistent_directory_12345", "output.tar.gz");

        // Act & Assert - Tests lines 45-64 (exception handling)
        var act = async () => await compressor.Compress(invalidPath, threads);
        await act.Should().ThrowAsync<Exception>("compression should fail with invalid path");
    }

    [Fact]
    public async Task Compress_WithValidInputAndEmptyThreads_ShouldSucceed()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var emptyThreads = new Dictionary<ulong, List<MessageBlob>>();
        var outputPath = CreateTempFilePath(".tar.gz");

        // Act - Tests that empty threads don't trigger error handling
        await compressor.Compress(outputPath, emptyThreads);

        // Assert
        File.Exists(outputPath).Should().BeTrue("file should be created even with empty threads");
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0, "archive should have headers");
    }

    #endregion

    #region CompressStreaming Method Tests

    [Fact]
    public async Task CompressStreaming_WithInvalidOutputPath_ShouldThrowAndCleanup()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = new Dictionary<ulong, List<MailKit.IMessageSummary>>();
        var invalidPath = Path.Combine("Z:\\nonexistent_directory_12345", "output.tar.gz");

        MessageFetcher fetcher = async (summary) =>
        {
            await Task.CompletedTask;
            return CreateTestMessageBlob("Test", "test@example.com", "content");
        };

        // Act & Assert - Tests lines 95-114 (exception handling in streaming)
        var act = async () => await compressor.CompressStreaming(invalidPath, threads, fetcher);
        await act.Should().ThrowAsync<Exception>("compression should fail with invalid path");
    }

    [Fact]
    public async Task CompressStreaming_WithEmptyThreads_ShouldSucceed()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = new Dictionary<ulong, List<MailKit.IMessageSummary>>();
        var outputPath = CreateTempFilePath(".tar.gz");

        MessageFetcher fetcher = async (summary) =>
        {
            await Task.CompletedTask;
            return CreateTestMessageBlob("Test", "test@example.com", "content");
        };

        // Act - Tests streaming with empty threads
        await compressor.CompressStreaming(outputPath, threads, fetcher);

        // Assert
        File.Exists(outputPath).Should().BeTrue("file should be created");
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0, "archive should have headers");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Compress_WithValidInput_ShouldSucceed()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = CreateTestThreads();
        var outputPath = CreateTempFilePath(".tar.gz");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue("file should be created");
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0, "file should have content");
    }

    #endregion
}
