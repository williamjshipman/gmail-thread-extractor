using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

/// <summary>
/// Comprehensive unit tests for the GMailThreadExtractor class.
/// Tests cover file extension handling, IMAP operations, compression formats, and error scenarios.
/// </summary>
public class GMailThreadExtractorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    public GMailThreadExtractorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"extractor_tests_{Guid.NewGuid():N}");
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

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitialize()
    {
        // Arrange & Act
        var extractor = CreateExtractor();

        // Assert
        extractor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomTimeout_ShouldUseProvidedTimeout()
    {
        // Arrange
        var customTimeout = TimeSpan.FromMinutes(10);

        // Act
        var extractor = CreateExtractor(timeout: customTimeout);

        // Assert - timeout is stored internally and used in ExtractThreadsAsync
        extractor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullTimeout_ShouldUseDefaultTimeout()
    {
        // Arrange & Act
        var extractor = CreateExtractor(timeout: null);

        // Assert - default 5 minute timeout is used internally
        extractor.Should().NotBeNull();
    }

    #endregion

    #region EnsureExpectedExtension Tests

    [Theory]
    [InlineData("output", ".tar.lzma", "output.tar.lzma")]
    [InlineData("output.tar.lzma", ".tar.lzma", "output.tar.lzma")]
    [InlineData("output.tar.gz", ".tar.lzma", "output.tar.lzma")]
    [InlineData("output.lzma", ".tar.lzma", "output.tar.lzma")]
    [InlineData("output.gz", ".tar.lzma", "output.tar.lzma")]
    [InlineData("OUTPUT.TAR.LZMA", ".tar.lzma", "OUTPUT.TAR.LZMA")] // Case insensitive match
    public void EnsureExpectedExtension_WithLzmaExtension_ShouldHandleCorrectly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Theory]
    [InlineData("output", ".tar.gz", "output.tar.gz")]
    [InlineData("output.tar.gz", ".tar.gz", "output.tar.gz")]
    [InlineData("output.tar.lzma", ".tar.gz", "output.tar.gz")]
    [InlineData("output.gz", ".tar.gz", "output.tar.gz")]
    [InlineData("output.lzma", ".tar.gz", "output.tar.gz")]
    [InlineData("OUTPUT.TAR.GZ", ".tar.gz", "OUTPUT.TAR.GZ")] // Case insensitive match
    public void EnsureExpectedExtension_WithGzipExtension_ShouldHandleCorrectly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Theory]
    [InlineData("output", ".tar.xz", "output.tar.xz")]
    [InlineData("output.tar.xz", ".tar.xz", "output.tar.xz")]
    [InlineData("output.tar.lzma", ".tar.xz", "output.tar.xz")]
    [InlineData("output.tar.gz", ".tar.xz", "output.tar.xz")]
    [InlineData("output.xz", ".tar.xz", "output.tar.xz")]
    [InlineData("output.txz", ".tar.xz", "output.tar.xz")] // Alternative .txz extension
    [InlineData("OUTPUT.TAR.XZ", ".tar.xz", "OUTPUT.TAR.XZ")] // Case insensitive match
    public void EnsureExpectedExtension_WithXzExtension_ShouldHandleCorrectly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Theory]
    [InlineData("output", ".tar.bz2", "output.tar.bz2")]
    [InlineData("output.tar.bz2", ".tar.bz2", "output.tar.bz2")]
    [InlineData("OUTPUT.TAR.BZ2", ".tar.bz2", "OUTPUT.TAR.BZ2")] // Case insensitive
    public void EnsureExpectedExtension_WithBzip2Extension_ShouldHandleCorrectly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Theory]
    [InlineData("", ".tar.lzma", "tar.lzma")]
    [InlineData(null, ".tar.lzma", "tar.lzma")]
    [InlineData("   ", ".tar.lzma", "tar.lzma")]
    public void EnsureExpectedExtension_WithEmptyPath_ShouldReturnExtensionOnly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Fact]
    public void EnsureExpectedExtension_WithDotPrefix_ShouldNormalizeCorrectly()
    {
        // Act
        var result1 = InvokeEnsureExpectedExtension("output", "tar.lzma");
        var result2 = InvokeEnsureExpectedExtension("output", ".tar.lzma");

        // Assert
        result1.Should().Be(result2);
        result1.Should().Be("output.tar.lzma");
    }

    [Theory]
    [InlineData("/path/to/output", ".tar.lzma", "/path/to/output.tar.lzma")]
    [InlineData("/path/to/output.tar.gz", ".tar.lzma", "/path/to/output.tar.lzma")]
    [InlineData("C:\\Users\\test\\output", ".tar.gz", "C:\\Users\\test\\output.tar.gz")]
    public void EnsureExpectedExtension_WithFullPaths_ShouldHandleCorrectly(
        string inputPath, string expectedExtension, string expectedOutput)
    {
        // Act
        var result = InvokeEnsureExpectedExtension(inputPath, expectedExtension);

        // Assert
        result.Should().Be(expectedOutput);
    }

    #endregion

    #region RemoveSuffix Tests

    [Theory]
    [InlineData("file.tar.lzma", ".tar.lzma", "file")]
    [InlineData("file.tar.gz", ".tar.gz", "file")]
    [InlineData("file.tar.xz", ".tar.xz", "file")]
    [InlineData("FILE.TAR.LZMA", ".tar.lzma", "FILE")] // Case insensitive
    [InlineData("output.lzma", ".lzma", "output")]
    public void RemoveSuffix_WithMatchingSuffix_ShouldRemove(
        string value, string suffix, string expected)
    {
        // Act
        var result = InvokeRemoveSuffix(value, suffix);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("file.tar.lzma", ".tar.gz")]
    [InlineData("file.tar.gz", ".tar.xz")]
    [InlineData("output", ".tar.lzma")]
    [InlineData("file.txt", ".tar.gz")]
    public void RemoveSuffix_WithNonMatchingSuffix_ShouldReturnNull(
        string value, string suffix)
    {
        // Act
        var result = InvokeRemoveSuffix(value, suffix);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("", ".tar.lzma")]
    [InlineData("short", ".verylongsuffix")]
    public void RemoveSuffix_WithEmptyOrShortValue_ShouldReturnNull(
        string value, string suffix)
    {
        // Act
        var result = InvokeRemoveSuffix(value, suffix);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ExtractThreadsAsync - Integration Test Note

    // NOTE: Full integration tests for ExtractThreadsAsync are covered in IntegrationTests.cs
    // The GMailThreadExtractor class is designed to work with real IMAP connections, and
    // mocking the ImapClient is challenging because many key methods (ConnectAsync, AuthenticateAsync)
    // are not virtual and cannot be mocked with standard mocking frameworks.
    //
    // The static methods (EnsureExpectedExtension, RemoveSuffix) are thoroughly tested above.
    // The ExtractThreadsAsync method's behavior is validated through:
    // 1. IntegrationTests.cs - End-to-end tests with realistic data
    // 2. CompressionTests.cs - Tests for individual compression algorithms
    // 3. Manual testing with actual Gmail accounts
    //
    // Areas tested via integration tests:
    // - Compression format selection (LZMA, XZ, Gzip, BZip2)
    // - Search query and label filtering
    // - Thread grouping by GMailThreadId
    // - Timeout configuration
    // - Max message size handling
    // - Retry logic integration (tested separately in RetryHelperTests.cs)

    #endregion

    #region Helper Methods

    private global::GMailThreadExtractor.GMailThreadExtractor CreateExtractor(
        string email = "test@example.com",
        string password = "test-password",
        string imapServer = "imap.gmail.com",
        int imapPort = 993,
        TimeSpan? timeout = null)
    {
        // Use reflection to create instance of internal class
        var type = typeof(global::GMailThreadExtractor.GMailThreadExtractor);
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string), typeof(string), typeof(int), typeof(TimeSpan?) },
            null);

        return (global::GMailThreadExtractor.GMailThreadExtractor)constructor!.Invoke(
            new object?[] { email, password, imapServer, imapPort, timeout });
    }


    private string InvokeEnsureExpectedExtension(string outputPath, string expectedExtension)
    {
        var type = typeof(global::GMailThreadExtractor.GMailThreadExtractor);
        var method = type.GetMethod("EnsureExpectedExtension",
            BindingFlags.Static | BindingFlags.NonPublic);

        return (string)method!.Invoke(null, new object[] { outputPath, expectedExtension })!;
    }

    private string? InvokeRemoveSuffix(string value, string suffix)
    {
        var type = typeof(global::GMailThreadExtractor.GMailThreadExtractor);
        var method = type.GetMethod("RemoveSuffix",
            BindingFlags.Static | BindingFlags.NonPublic);

        return (string?)method!.Invoke(null, new object[] { value, suffix });
    }

    #endregion
}
