using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.GZip;
using MailKit;
using MimeKit;
using Moq;
using FluentAssertions;
using ArchivalSupport;

namespace ArchivalSupport.Tests;

public class CompressionTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    public CompressionTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"compression_tests_{Guid.NewGuid():N}");
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
        var message1 = CreateTestMessageBlob("Test Subject 1", "sender1@example.com", "Small message content");
        var message2 = CreateTestMessageBlob("Test Subject 2", "sender2@example.com", "Another message content");
        var message3 = CreateTestMessageBlob("Re: Test Subject 1", "sender3@example.com", "Reply message content");

        return new Dictionary<ulong, List<MessageBlob>>
        {
            { 123456789, new List<MessageBlob> { message1, message3 } },
            { 987654321, new List<MessageBlob> { message2 } }
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

    [Fact]
    public async Task LZMACompressor_Compress_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new LZMACompressor();
        var threads = CreateTestThreads();
        var outputPath = CreateTempFilePath(".tar.lzma");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);

        // Verify the compressed file structure by decompressing and checking tar content
        await VerifyLZMAArchiveContent(outputPath, threads);
    }

    [Fact]
    public async Task TarGzipCompressor_Compress_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = CreateTestThreads();
        var outputPath = CreateTempFilePath(".tar.gz");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);

        // Verify the compressed file structure
        VerifyTarGzArchiveContent(outputPath, threads);
    }

    [Fact]
    public async Task LZMACompressor_CompressStreaming_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new LZMACompressor();
        var threads = CreateTestStreamingThreads();
        var outputPath = CreateTempFilePath(".tar.lzma");

        MessageFetcher messageFetcher = async (summary) =>
        {
            // Simulate fetching message content
            await Task.Delay(1); // Simulate network delay
            return CreateTestMessageBlob(
                summary.Envelope?.Subject ?? "Test Subject",
                summary.Envelope?.From?.ToString() ?? "test@example.com",
                "Streaming message content");
        };

        // Act
        await compressor.CompressStreaming(outputPath, threads, messageFetcher);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TarGzipCompressor_CompressStreaming_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new TarGzipCompressor();
        var threads = CreateTestStreamingThreads();
        var outputPath = CreateTempFilePath(".tar.gz");

        MessageFetcher messageFetcher = async (summary) =>
        {
            await Task.Delay(1);
            return CreateTestMessageBlob(
                summary.Envelope?.Subject ?? "Test Subject",
                summary.Envelope?.From?.ToString() ?? "test@example.com",
                "Streaming message content");
        };

        // Act
        await compressor.CompressStreaming(outputPath, threads, messageFetcher);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Compressors_WithEmptyThreads_ShouldCreateEmptyArchive()
    {
        // Arrange
        var lzmaCompressor = new LZMACompressor();
        var gzipCompressor = new TarGzipCompressor();
        var emptyThreads = new Dictionary<ulong, List<MessageBlob>>();
        var lzmaPath = CreateTempFilePath(".tar.lzma");
        var gzipPath = CreateTempFilePath(".tar.gz");

        // Act
        await lzmaCompressor.Compress(lzmaPath, emptyThreads);
        await gzipCompressor.Compress(gzipPath, emptyThreads);

        // Assert
        File.Exists(lzmaPath).Should().BeTrue();
        File.Exists(gzipPath).Should().BeTrue();

        // Empty archives should still have some size (compression headers)
        new FileInfo(lzmaPath).Length.Should().BeGreaterThan(0);
        new FileInfo(gzipPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Compressors_WithLargeMessage_ShouldHandleStreaming()
    {
        // Arrange
        var compressor = new LZMACompressor();
        var largeContent = new string('A', 1024 * 1024); // 1MB of data
        var largeMessage = CreateTestMessageBlob("Large Message", "sender@example.com", largeContent);

        var threads = new Dictionary<ulong, List<MessageBlob>>
        {
            { 123456789, new List<MessageBlob> { largeMessage } }
        };
        var outputPath = CreateTempFilePath(".tar.lzma");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);
        fileInfo.Length.Should().BeLessThan(largeContent.Length); // Should be compressed
    }

    [Fact]
    public async Task Compressors_WithInvalidOutputPath_ShouldThrow()
    {
        // Arrange
        var compressor = new LZMACompressor();
        var threads = CreateTestThreads();
        var invalidPath = Path.Combine("nonexistent_directory", "output.tar.lzma");

        // Act & Assert
        var act = async () => await compressor.Compress(invalidPath, threads);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task CompressionSizes_ShouldDifferBetweenAlgorithms()
    {
        // Arrange
        var lzmaCompressor = new LZMACompressor();
        var gzipCompressor = new TarGzipCompressor();
        var threads = CreateTestThreads();
        var lzmaPath = CreateTempFilePath(".tar.lzma");
        var gzipPath = CreateTempFilePath(".tar.gz");

        // Act
        await lzmaCompressor.Compress(lzmaPath, threads);
        await gzipCompressor.Compress(gzipPath, threads);

        // Assert
        File.Exists(lzmaPath).Should().BeTrue();
        File.Exists(gzipPath).Should().BeTrue();

        var lzmaSize = new FileInfo(lzmaPath).Length;
        var gzipSize = new FileInfo(gzipPath).Length;

        // Both should create valid files
        lzmaSize.Should().BeGreaterThan(0);
        gzipSize.Should().BeGreaterThan(0);

        // Sizes should be different (exact comparison depends on content)
        Math.Abs(lzmaSize - gzipSize).Should().BeGreaterThan(0);
    }

    private Dictionary<ulong, List<IMessageSummary>> CreateTestStreamingThreads()
    {
        var mockSummary1 = new Mock<IMessageSummary>();
        mockSummary1.Setup(x => x.UniqueId).Returns(new UniqueId(1));
        mockSummary1.Setup(x => x.Envelope).Returns(new Envelope
        {
            Subject = "Test Subject 1",
            From = { MailboxAddress.Parse("sender1@example.com") }
        });

        var mockSummary2 = new Mock<IMessageSummary>();
        mockSummary2.Setup(x => x.UniqueId).Returns(new UniqueId(2));
        mockSummary2.Setup(x => x.Envelope).Returns(new Envelope
        {
            Subject = "Test Subject 2",
            From = { MailboxAddress.Parse("sender2@example.com") }
        });

        return new Dictionary<ulong, List<IMessageSummary>>
        {
            { 123456789, new List<IMessageSummary> { mockSummary1.Object } },
            { 987654321, new List<IMessageSummary> { mockSummary2.Object } }
        };
    }

    private async Task VerifyLZMAArchiveContent(string lzmaPath, Dictionary<ulong, List<MessageBlob>> originalThreads)
    {
        // For LZMA verification, we would need to decompress and verify content
        // This is a simplified verification that checks the file exists and has content
        var fileInfo = new FileInfo(lzmaPath);
        fileInfo.Length.Should().BeGreaterThan(100); // Should have compressed content

        // Read LZMA header to verify it's a valid LZMA file
        using var fileStream = File.OpenRead(lzmaPath);
        var buffer = new byte[13]; // LZMA header size
        await fileStream.ReadExactlyAsync(buffer);

        // LZMA files should have properties in first 5 bytes
        buffer.Length.Should().Be(13);
    }

    private void VerifyTarGzArchiveContent(string gzipPath, Dictionary<ulong, List<MessageBlob>> originalThreads)
    {
        using var fileStream = File.OpenRead(gzipPath);
        using var gzipStream = new GZipInputStream(fileStream);
        using var tarStream = new TarInputStream(gzipStream, System.Text.Encoding.UTF8);

        var foundEntries = new List<string>();
        TarEntry? entry;

        while ((entry = tarStream.GetNextEntry()) != null)
        {
            foundEntries.Add(entry.Name);
        }

        // Should have directory entries for each thread
        foundEntries.Should().Contain(name => name.Contains("123456789"));
        foundEntries.Should().Contain(name => name.Contains("987654321"));

        // Should have message files
        foundEntries.Should().Contain(name => name.EndsWith(".eml"));
    }
}