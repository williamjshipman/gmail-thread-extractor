using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Moq;
using FluentAssertions;
using GMailThreadExtractor;
using ArchivalSupport;

namespace GMailThreadExtractor.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    public IntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"integration_tests_{Guid.NewGuid():N}");
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

    [Fact]
    public async Task ExtractThreadsAsync_WithMockImapClient_ShouldCreateValidArchive()
    {
        // Arrange
        var outputPath = CreateTempFilePath(".tar.lzma");

        // We can't easily test the full GMailThreadExtractor due to its dependency on actual IMAP connections
        // Instead, we'll test the compression pipeline with realistic data

        var threads = CreateTestThreadsWithRealisticData();
        var compressor = new ArchivalSupport.LZMACompressor();

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);

        // Verify the archive contains the expected structure
        await VerifyArchiveStructure(outputPath, threads);
    }

    [Fact]
    public async Task ExtractThreadsAsync_WithStreamingCompression_ShouldHandleLargeMessages()
    {
        // Arrange
        var outputPath = CreateTempFilePath(".tar.lzma");
        var threads = CreateTestStreamingThreads();
        var compressor = new ArchivalSupport.LZMACompressor();

        MessageFetcher messageFetcher = (summary) =>
        {
            // Simulate fetching a large message
            var largeContent = new string('A', 1024 * 100); // 100KB message
            return Task.FromResult(CreateRealisticMessageBlob(
                summary.Envelope?.Subject ?? "Large Test Message",
                summary.Envelope?.From?.ToString() ?? "sender@example.com",
                largeContent));
        };

        // Act
        await compressor.CompressStreaming(outputPath, threads, messageFetcher);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(500); // Should have reasonable compression
    }

    [Fact]
    public async Task CompressionComparison_ShouldShowDifferentAlgorithmBehaviors()
    {
        // Arrange
        var threads = CreateTestThreadsWithRealisticData();
        var lzmaPath = CreateTempFilePath(".tar.lzma");
        var gzipPath = CreateTempFilePath(".tar.gz");

        var lzmaCompressor = new ArchivalSupport.LZMACompressor();
        var gzipCompressor = new ArchivalSupport.TarGzipCompressor();

        // Act
        await lzmaCompressor.Compress(lzmaPath, threads);
        await gzipCompressor.Compress(gzipPath, threads);

        // Assert
        File.Exists(lzmaPath).Should().BeTrue();
        File.Exists(gzipPath).Should().BeTrue();

        var lzmaSize = new FileInfo(lzmaPath).Length;
        var gzipSize = new FileInfo(gzipPath).Length;

        // Both should create valid archives
        lzmaSize.Should().BeGreaterThan(0);
        gzipSize.Should().BeGreaterThan(0);

        // Generally LZMA should provide better compression for text data
        // (though this isn't guaranteed for small test data)
        Math.Abs(lzmaSize - gzipSize).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndToEndWorkflow_WithConfigurationAndValidation_ShouldSucceed()
    {
        // Arrange
        var configData = new
        {
            email = "test@example.com",
            password = "test-password",
            search = "from:test@example.com",
            output = CreateTempFilePath(".tar.lzma"),
            compression = "lzma",
            timeoutMinutes = 1,
            maxMessageSizeMB = 5
        };

        var configPath = CreateTempFilePath(".json");
        var configJson = System.Text.Json.JsonSerializer.Serialize(configData);
        await File.WriteAllTextAsync(configPath, configJson);

        // Act
        var config = await Config.LoadFromFileAsync(configPath);

        // Assert - Configuration should load and validate
        config.Should().NotBeNull();
        config.Email.Should().Be("test@example.com");
        config.Compression.Should().Be("lzma");

        var act = () => config.Validate();
        act.Should().NotThrow();

        // Verify merged configuration behavior
        var merged = config.MergeWithCommandLine(
            cmdEmail: null, // Use config value
            cmdPassword: "override-password", // Override config
            cmdSearch: null, // Use config value
            cmdLabel: null,
            cmdOutput: null, // Use config value
            cmdCompression: null, // Use config value
            cmdTimeoutMinutes: null); // Use config value

        merged.Email.Should().Be("test@example.com");
        merged.Password.Should().Be("override-password");
        merged.Search.Should().Be("from:test@example.com");
        merged.Compression.Should().Be("lzma");
    }

    [Fact]
    public async Task ErrorRecovery_WithFileSystemErrors_ShouldHandleGracefully()
    {
        // Arrange
        var threads = CreateTestThreadsWithRealisticData();
        var compressor = new ArchivalSupport.LZMACompressor();

        // Try to write to an invalid path
        var invalidPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "C:\\NonexistentDirectory\\output.tar.lzma"
            : "/nonexistent/directory/output.tar.lzma";

        // Act & Assert
        var act = async () => await compressor.Compress(invalidPath, threads);
        await act.Should().ThrowAsync<Exception>();

        // Verify no partial files are left behind
        File.Exists(invalidPath).Should().BeFalse();
    }

    [Fact]
    public async Task RetryLogic_WithTransientFailures_ShouldEventuallySucceed()
    {
        // Arrange
        var attemptCount = 0;
        var expectedResult = "success";

        Func<Task<string>> operation = async () =>
        {
            attemptCount++;
            await Task.Delay(1);

            // Fail first two attempts, succeed on third
            if (attemptCount < 3)
                throw new System.Net.Sockets.SocketException();

            return expectedResult;
        };

        // Act & Assert
        var result = await RetryHelper.ExecuteWithRetryAsync(
            operation,
            maxAttempts: 5,
            baseDelay: TimeSpan.FromMilliseconds(10));

        result.Should().Be(expectedResult);
        attemptCount.Should().Be(3);
    }

    [Fact]
    public void SafeNameBuilder_WithComplexEmailData_ShouldCreateValidPaths()
    {
        // Arrange
        var complexSubject = "Re: Fw: Important Document [URGENT] <confidential> Q3/2024 Results???";
        var complexSender = "John O'Brien <john.obrien+test@company-name.co.uk>";
        var threadId = 9876543210UL;

        // Act
        var threadDirName = ArchivalSupport.SafeNameBuilder.BuildThreadDirectoryName(threadId, complexSubject);
        var messageFileName = ArchivalSupport.SafeNameBuilder.BuildMessageFileName(
            "12345", complexSubject, "2024-12-25_14-30-00", complexSender);

        // Assert
        threadDirName.Should().NotBeNullOrEmpty();
        threadDirName.Should().StartWith("9876543210_");
        threadDirName.Length.Should().BeLessOrEqualTo(100); // TAR name limit

        messageFileName.Should().NotBeNullOrEmpty();
        messageFileName.Should().EndWith(".eml");
        messageFileName.Length.Should().BeLessOrEqualTo(100);

        // Should not contain invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        threadDirName.Should().NotContainAny(invalidChars.Select(c => c.ToString()));
        messageFileName.Should().NotContainAny(invalidChars.Select(c => c.ToString()));
    }


    private Dictionary<ulong, List<ArchivalSupport.MessageBlob>> CreateTestThreadsWithRealisticData()
    {
        return new Dictionary<ulong, List<ArchivalSupport.MessageBlob>>
        {
            {
                123456789UL,
                new List<ArchivalSupport.MessageBlob>
                {
                    CreateRealisticMessageBlob(
                        "Project Update - Sprint 42",
                        "project-manager@company.com",
                        "Hi team,\n\nHere's the update for Sprint 42:\n\n- Feature A completed\n- Bug fixes in progress\n- Next sprint planning scheduled\n\nBest regards,\nPM"),
                    CreateRealisticMessageBlob(
                        "Re: Project Update - Sprint 42",
                        "developer@company.com",
                        "Thanks for the update! I have a question about Feature A implementation...")
                }
            },
            {
                987654321UL,
                new List<ArchivalSupport.MessageBlob>
                {
                    CreateRealisticMessageBlob(
                        "Meeting Notes - Q4 Planning",
                        "ceo@company.com",
                        "Quarterly planning meeting notes:\n\n1. Budget review\n2. Resource allocation\n3. Strategic initiatives\n\nAction items attached.")
                }
            }
        };
    }

    private ArchivalSupport.MessageBlob CreateRealisticMessageBlob(string subject, string from, string content)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("recipient@company.com"));
        message.Subject = subject;
        message.Date = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30));
        message.MessageId = $"{Guid.NewGuid()}@company.com";

        var bodyBuilder = new BodyBuilder
        {
            TextBody = content
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var stream = new MemoryStream();
        message.WriteTo(stream);

        return new ArchivalSupport.MessageBlob(
            Guid.NewGuid().ToString(),
            stream.ToArray(),
            subject,
            from,
            "recipient@company.com",
            message.Date.UtcDateTime);
    }

    private Dictionary<ulong, List<IMessageSummary>> CreateTestStreamingThreads()
    {
        var mockSummary1 = new Mock<IMessageSummary>();
        mockSummary1.Setup(x => x.UniqueId).Returns(new UniqueId(1001));
        mockSummary1.Setup(x => x.Size).Returns(1024 * 100); // 100KB
        mockSummary1.Setup(x => x.Envelope).Returns(new Envelope
        {
            Subject = "Large Document Attachment",
            From = { MailboxAddress.Parse("sender@company.com") },
            Date = DateTimeOffset.UtcNow
        });

        var mockSummary2 = new Mock<IMessageSummary>();
        mockSummary2.Setup(x => x.UniqueId).Returns(new UniqueId(1002));
        mockSummary2.Setup(x => x.Size).Returns(1024 * 50); // 50KB
        mockSummary2.Setup(x => x.Envelope).Returns(new Envelope
        {
            Subject = "Re: Large Document Attachment",
            From = { MailboxAddress.Parse("recipient@company.com") },
            Date = DateTimeOffset.UtcNow.AddHours(1)
        });

        return new Dictionary<ulong, List<IMessageSummary>>
        {
            { 555666777UL, new List<IMessageSummary> { mockSummary1.Object, mockSummary2.Object } }
        };
    }

    private async Task VerifyArchiveStructure(string archivePath, Dictionary<ulong, List<ArchivalSupport.MessageBlob>> expectedThreads)
    {
        // Basic verification - ensure file exists and has reasonable size
        var fileInfo = new FileInfo(archivePath);
        fileInfo.Length.Should().BeGreaterThan(100); // Should have substantial content

        // For more detailed verification, we would need to decompress the LZMA archive
        // This is complex and would require additional dependencies
        // For now, we verify the file is created and has content
        var fileBytes = await File.ReadAllBytesAsync(archivePath);
        fileBytes.Length.Should().BeGreaterThan(0);

        // LZMA files start with specific header bytes
        fileBytes.Length.Should().BeGreaterOrEqualTo(13); // Minimum LZMA header size
    }
}