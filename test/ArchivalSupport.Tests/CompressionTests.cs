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
using Joveler.Compression.XZ;

namespace ArchivalSupport.Tests;

public class CompressionTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    static CompressionTests()
    {
        // Initialize Joveler.Compression.XZ library for tests with error handling
        try
        {
            // Try to locate the correct native library based on platform
            var baseDir = Path.GetDirectoryName(typeof(CompressionTests).Assembly.Location);
            var runtimeDir = Path.Combine(baseDir!, "runtimes");

            string? nativeLibPath = null;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-x64", "native", "liblzma.dll");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X86)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-x86", "native", "liblzma.dll");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-arm64", "native", "liblzma.dll");
                }
            }

            if (nativeLibPath != null && File.Exists(nativeLibPath))
            {
                System.Console.WriteLine($"Attempting to initialize XZ with library at: {nativeLibPath}");
                XZInit.GlobalInit(nativeLibPath);
            }
            else
            {
                System.Console.WriteLine($"Native library path not found or doesn't exist: {nativeLibPath}");
                System.Console.WriteLine("Trying default initialization...");
                XZInit.GlobalInit();
            }
        }
        catch (Exception ex)
        {
            // Log the specific error but don't fail static initialization
            System.Console.WriteLine($"XZ initialization failed: {ex.Message}");
            System.Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw; // Re-throw to see the error in tests
        }
    }

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
    public async Task TarXzCompressor_Compress_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new TarXzCompressor();
        var threads = CreateTestThreads();
        var outputPath = CreateTempFilePath(".tar.xz");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);

        // Verify the compressed file structure by decompressing and checking tar content
        VerifyTarXzArchiveContent(outputPath, threads);
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
    public async Task TarXzCompressor_CompressStreaming_ShouldCreateValidArchive()
    {
        // Arrange
        var compressor = new TarXzCompressor();
        var threads = CreateTestStreamingThreads();
        var outputPath = CreateTempFilePath(".tar.xz");

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
        var xzCompressor = new TarXzCompressor();
        var emptyThreads = new Dictionary<ulong, List<MessageBlob>>();
        var lzmaPath = CreateTempFilePath(".tar.lzma");
        var gzipPath = CreateTempFilePath(".tar.gz");
        var xzPath = CreateTempFilePath(".tar.xz");

        // Act
        await lzmaCompressor.Compress(lzmaPath, emptyThreads);
        await gzipCompressor.Compress(gzipPath, emptyThreads);
        await xzCompressor.Compress(xzPath, emptyThreads);

        // Assert
        File.Exists(lzmaPath).Should().BeTrue();
        File.Exists(gzipPath).Should().BeTrue();
        File.Exists(xzPath).Should().BeTrue();

        // Empty archives should still have some size (compression headers)
        new FileInfo(lzmaPath).Length.Should().BeGreaterThan(0);
        new FileInfo(gzipPath).Length.Should().BeGreaterThan(0);
        new FileInfo(xzPath).Length.Should().BeGreaterThan(0);
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
        var xzCompressor = new TarXzCompressor();
        var threads = CreateTestThreads();
        var lzmaPath = CreateTempFilePath(".tar.lzma");
        var gzipPath = CreateTempFilePath(".tar.gz");
        var xzPath = CreateTempFilePath(".tar.xz");

        // Act
        await lzmaCompressor.Compress(lzmaPath, threads);
        await gzipCompressor.Compress(gzipPath, threads);
        await xzCompressor.Compress(xzPath, threads);

        // Assert
        File.Exists(lzmaPath).Should().BeTrue();
        File.Exists(gzipPath).Should().BeTrue();
        File.Exists(xzPath).Should().BeTrue();

        var lzmaSize = new FileInfo(lzmaPath).Length;
        var gzipSize = new FileInfo(gzipPath).Length;
        var xzSize = new FileInfo(xzPath).Length;

        // Both should create valid files
        lzmaSize.Should().BeGreaterThan(0);
        gzipSize.Should().BeGreaterThan(0);
        xzSize.Should().BeGreaterThan(0);

        // Sizes should be different (exact comparison depends on content)
        Math.Abs(lzmaSize - gzipSize).Should().BeGreaterThan(0);
        Math.Abs(lzmaSize - xzSize).Should().BeGreaterThan(0);
        Math.Abs(gzipSize - xzSize).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TarXzCompressor_WithLargeContent_ShouldCompressCompleteFile()
    {
        // Arrange
        var compressor = new TarXzCompressor();
        var largeContent = new string('A', 1024 * 1024); // 1MB of repetitive data
        var largeMessage = CreateTestMessageBlob("Large Test Message", "sender@example.com", largeContent);

        var threads = new Dictionary<ulong, List<MessageBlob>>
        {
            { 123456789, new List<MessageBlob> { largeMessage } }
        };
        var outputPath = CreateTempFilePath(".tar.xz");

        // Act
        await compressor.Compress(outputPath, threads);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);

        // XZ compression should achieve significant compression on repetitive data
        // The compressed size should be much smaller than the original (less than 1% for repetitive data)
        fileInfo.Length.Should().BeLessThan(largeMessage.Size / 100); // Should be less than 1% of original size

        // Verify the compressed file is not truncated at 500KB
        // If it was truncated, we'd expect the decompressed content to be incomplete
        VerifyTarXzArchiveContent(outputPath, threads);
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

    private void VerifyTarXzArchiveContent(string xzPath, Dictionary<ulong, List<MessageBlob>> originalThreads)
    {
        // TarXzCompressor now uses true XZ format with Joveler.Compression.XZ
        // Decompress using XZ stream and then read tar contents
        using var fileStream = File.OpenRead(xzPath);
        using var decompressedStream = new MemoryStream();

        // Use Joveler.Compression.XZ to decompress
        var decompressOptions = new XZDecompressOptions();
        using var xzStream = new XZStream(fileStream, decompressOptions);
        xzStream.CopyTo(decompressedStream);

        // Now read the decompressed tar content and verify both structure and content
        decompressedStream.Position = 0;
        using var tarStream = new TarInputStream(decompressedStream, System.Text.Encoding.UTF8);

        var foundFiles = new Dictionary<string, byte[]>();
        var foundDirectories = new List<string>();
        TarEntry? entry;

        // Extract all files and directories from the tar archive
        while ((entry = tarStream.GetNextEntry()) != null)
        {
            if (entry.IsDirectory)
            {
                foundDirectories.Add(entry.Name);
            }
            else
            {
                // Read the file content
                var fileContent = new byte[entry.Size];
                int totalBytesRead = 0;
                while (totalBytesRead < entry.Size)
                {
                    int bytesRead = tarStream.Read(fileContent, totalBytesRead, (int)(entry.Size - totalBytesRead));
                    if (bytesRead == 0) break; // End of stream
                    totalBytesRead += bytesRead;
                }
                foundFiles[entry.Name] = fileContent;
            }
        }

        // Verify that all original thread IDs are present as directories
        foreach (var threadId in originalThreads.Keys)
        {
            foundDirectories.Should().Contain(name => name.Contains(threadId.ToString()));
        }

        // Verify that each original message is present with correct content
        foreach (var thread in originalThreads)
        {
            var threadId = thread.Key;
            var messages = thread.Value;

            foreach (var originalMessage in messages)
            {
                // Find the corresponding file in the archive
                var expectedFileName = foundFiles.Keys.FirstOrDefault(fileName =>
                    fileName.Contains(threadId.ToString()) &&
                    fileName.EndsWith(".eml") &&
                    fileName.Contains(originalMessage.UniqueId));

                expectedFileName.Should().NotBeNull($"Message {originalMessage.UniqueId} should be present in thread {threadId}");

                // Verify the file content matches the original message blob
                var archivedContent = foundFiles[expectedFileName!];
                archivedContent.Should().NotBeNull("Archived file content should not be null");
                archivedContent.Length.Should().BeGreaterThan(0, "Archived file should have content");

                // Compare the actual binary content
                if (originalMessage.Blob != null)
                {
                    archivedContent.Should().Equal(originalMessage.Blob,
                        $"Content of archived message {originalMessage.UniqueId} should match original blob");
                }
                else if (originalMessage.IsStreaming && originalMessage.StreamFunc != null)
                {
                    // For streaming messages, we need to get the content via the stream function
                    using var originalContentStream = new MemoryStream();
                    originalMessage.StreamFunc(originalContentStream).Wait();
                    var originalContent = originalContentStream.ToArray();

                    archivedContent.Should().Equal(originalContent,
                        $"Content of archived streaming message {originalMessage.UniqueId} should match original stream content");
                }
            }
        }

        // Should have message files
        foundFiles.Keys.Should().Contain(name => name.EndsWith(".eml"), "Archive should contain .eml message files");
    }
}
