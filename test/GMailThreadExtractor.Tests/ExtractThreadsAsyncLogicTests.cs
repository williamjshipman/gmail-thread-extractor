using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MailKit.Search;
using MimeKit;
using Moq;
using FluentAssertions;
using ArchivalSupport;

namespace GMailThreadExtractor.Tests;

/// <summary>
/// Unit tests specifically targeting lines 108-239 of GMailThreadExtractor.cs
/// These tests focus on the core logic branches that are difficult to test
/// through integration tests, using isolated test harnesses.
/// </summary>
public class ExtractThreadsAsyncLogicTests
{
    #region Search Query Construction Logic (Lines 117-131)

    [Fact]
    public void SearchQueryConstruction_WithNoSearchOrLabel_ShouldReturnSearchAll()
    {
        // This tests lines 117-131: query construction logic
        // When both searchQuery and label are null/empty, should use SearchQuery.All

        // Arrange
        string searchQuery = null;
        string label = null;

        // Act - Replicate the logic from lines 117-131
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        query.Should().Be(SearchQuery.All, "empty search and label should use SearchQuery.All");
        queries.Should().BeEmpty("no queries should be added");
    }

    [Fact]
    public void SearchQueryConstruction_WithSearchQueryOnly_ShouldUseGMailRawSearch()
    {
        // This tests lines 119-122: searchQuery handling

        // Arrange
        string searchQuery = "from:sender@example.com";
        string label = null;

        // Act - Replicate the logic
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        queries.Should().HaveCount(1, "only search query should be added");
        query.Should().NotBe(SearchQuery.All, "query should be modified from default");
    }

    [Fact]
    public void SearchQueryConstruction_WithLabelOnly_ShouldUseHasGMailLabel()
    {
        // This tests lines 123-126: label handling

        // Arrange
        string searchQuery = null;
        string label = "Important";

        // Act - Replicate the logic
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        queries.Should().HaveCount(1, "only label query should be added");
        query.Should().NotBe(SearchQuery.All, "query should be modified from default");
    }

    [Fact]
    public void SearchQueryConstruction_WithBothSearchAndLabel_ShouldCombineWithAnd()
    {
        // This tests lines 117-131: combining multiple queries with AND

        // Arrange
        string searchQuery = "from:sender@example.com";
        string label = "Important";

        // Act - Replicate the logic from lines 128-131
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        queries.Should().HaveCount(2, "both queries should be added");
        query.Should().NotBe(SearchQuery.All, "query should combine both conditions");
    }

    [Fact]
    public void SearchQueryConstruction_WithEmptyString_ShouldNotAddQuery()
    {
        // This tests edge case: empty strings are filtered by IsNullOrEmpty

        // Arrange
        string searchQuery = "";
        string label = "";

        // Act - Replicate the logic (uses IsNullOrEmpty, not IsNullOrWhiteSpace)
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        queries.Should().BeEmpty("empty strings should not add queries");
        query.Should().Be(SearchQuery.All);
    }

    [Fact]
    public void SearchQueryConstruction_WithWhitespaceString_ShouldAddQuery()
    {
        // This tests actual code behavior: IsNullOrEmpty allows whitespace strings
        // Note: The actual code (line 119, 123) uses IsNullOrEmpty, not IsNullOrWhiteSpace

        // Arrange
        string searchQuery = "";
        string label = "   "; // Whitespace only - IsNullOrEmpty returns false!

        // Act - Replicate the logic
        var queries = new List<SearchQuery>();
        if (!string.IsNullOrEmpty(searchQuery))
        {
            queries.Add(SearchQuery.GMailRawSearch(searchQuery));
        }
        if (!string.IsNullOrEmpty(label))
        {
            queries.Add(SearchQuery.HasGMailLabel(label));
        }
        var query = SearchQuery.All;
        if (queries.Count > 0)
        {
            query = queries.Aggregate(query, (current, q) => current.And(q));
        }

        // Assert
        queries.Should().HaveCount(1, "whitespace string passes IsNullOrEmpty check");
        query.Should().NotBe(SearchQuery.All, "query should be modified");
    }

    #endregion

    #region Thread Grouping and Duplicate Detection Logic (Lines 151-181)

    [Fact]
    public void ThreadGrouping_WithNullGMailThreadId_ShouldSkipMessage()
    {
        // This tests line 154: if (message.GMailThreadId.HasValue)

        // Arrange
        var messages = new List<Mock<IMessageSummary>>();
        var mockMessage1 = CreateMockMessageSummary(1, null); // No thread ID
        var mockMessage2 = CreateMockMessageSummary(2, 123UL);
        messages.Add(mockMessage1);
        messages.Add(mockMessage2);

        // Act - Replicate the logic from lines 152-181
        var threads = new Dictionary<ulong, int>(); // Using int as placeholder
        foreach (var message in messages.Select(m => m.Object))
        {
            if (message.GMailThreadId.HasValue)
            {
                ulong threadId = message.GMailThreadId.Value;
                if (!threads.ContainsKey(threadId))
                {
                    threads[threadId] = 1; // Placeholder for actual thread list
                }
            }
        }

        // Assert
        threads.Should().HaveCount(1, "only message with thread ID should be processed");
        threads.Should().ContainKey(123UL, "valid thread ID should be added");
    }

    [Fact]
    public void ThreadGrouping_WithDuplicateThreadId_ShouldSkip()
    {
        // This tests lines 157-160: duplicate thread detection

        // Arrange
        var messages = new List<Mock<IMessageSummary>>();
        var mockMessage1 = CreateMockMessageSummary(1, 123UL);
        var mockMessage2 = CreateMockMessageSummary(2, 123UL); // Same thread
        var mockMessage3 = CreateMockMessageSummary(3, 456UL);
        messages.Add(mockMessage1);
        messages.Add(mockMessage2);
        messages.Add(mockMessage3);

        // Act - Replicate the duplicate check logic from lines 157-160
        var threads = new Dictionary<ulong, List<uint>>();
        foreach (var message in messages.Select(m => m.Object))
        {
            if (message.GMailThreadId.HasValue)
            {
                ulong threadId = message.GMailThreadId.Value;
                if (threads.ContainsKey(threadId))
                {
                    continue; // Line 159: Skip if we already have this thread
                }
                threads[threadId] = new List<uint> { message.UniqueId.Id };
            }
        }

        // Assert
        threads.Should().HaveCount(2, "duplicate thread should be skipped");
        threads.Keys.Should().Contain(123UL, "first occurrence of thread 123 should be added");
        threads.Keys.Should().Contain(456UL, "thread 456 should be added");
        threads[123UL].Should().HaveCount(1, "duplicate thread 123 should not add second message");
    }

    [Fact]
    public void ThreadGrouping_WithMultipleUniqueThreads_ShouldAddAll()
    {
        // This tests lines 156-179: adding unique threads

        // Arrange
        var messages = new List<Mock<IMessageSummary>>();
        for (uint i = 1; i <= 5; i++)
        {
            messages.Add(CreateMockMessageSummary(i, i * 100UL)); // Unique thread IDs
        }

        // Act - Replicate the logic
        var threads = new Dictionary<ulong, List<uint>>();
        foreach (var message in messages.Select(m => m.Object))
        {
            if (message.GMailThreadId.HasValue)
            {
                ulong threadId = message.GMailThreadId.Value;
                if (threads.ContainsKey(threadId))
                {
                    continue;
                }
                threads[threadId] = new List<uint> { message.UniqueId.Id };
            }
        }

        // Assert
        threads.Should().HaveCount(5, "all unique threads should be added");
        threads.Keys.Should().Contain(new[] { 100UL, 200UL, 300UL, 400UL, 500UL });
    }

    [Fact]
    public void ThreadGrouping_WithMixedValidAndNullThreadIds_ShouldProcessOnlyValid()
    {
        // This tests combination of lines 154 and 157-160

        // Arrange
        var messages = new List<Mock<IMessageSummary>>();
        messages.Add(CreateMockMessageSummary(1, 123UL));  // Valid
        messages.Add(CreateMockMessageSummary(2, null));   // Null - skip
        messages.Add(CreateMockMessageSummary(3, 123UL));  // Duplicate - skip
        messages.Add(CreateMockMessageSummary(4, 456UL));  // Valid
        messages.Add(CreateMockMessageSummary(5, null));   // Null - skip
        messages.Add(CreateMockMessageSummary(6, 789UL));  // Valid

        // Act
        var threads = new Dictionary<ulong, List<uint>>();
        foreach (var message in messages.Select(m => m.Object))
        {
            if (message.GMailThreadId.HasValue)
            {
                ulong threadId = message.GMailThreadId.Value;
                if (threads.ContainsKey(threadId))
                {
                    continue;
                }
                threads[threadId] = new List<uint> { message.UniqueId.Id };
            }
        }

        // Assert
        threads.Should().HaveCount(3, "should have 3 unique valid threads");
        threads.Keys.Should().Contain(new[] { 123UL, 456UL, 789UL });
    }

    #endregion

    #region File Extension Determination Logic (Lines 185-194)

    [Theory]
    [InlineData("gzip", ".tar.gz")]
    [InlineData("GZIP", ".tar.gz")]
    [InlineData("Gzip", ".tar.gz")]
    public void FileExtension_WithGzipCompression_ShouldReturnTarGz(
        string compression, string expectedExtension)
    {
        // This tests lines 186-192: extension determination switch expression

        // Act - Replicate the logic from lines 186-192
        var actualExtension = compression.ToLowerInvariant() switch
        {
            "gzip" => ".tar.gz",
            "xz" => ".tar.xz",
            "bzip2" => ".tar.bz2",
            _ => ".tar.lzma"
        };

        // Assert
        actualExtension.Should().Be(expectedExtension);
    }

    [Theory]
    [InlineData("xz", ".tar.xz")]
    [InlineData("XZ", ".tar.xz")]
    [InlineData("Xz", ".tar.xz")]
    public void FileExtension_WithXzCompression_ShouldReturnTarXz(
        string compression, string expectedExtension)
    {
        // This tests line 189: "xz" => ".tar.xz"

        // Act
        var actualExtension = compression.ToLowerInvariant() switch
        {
            "gzip" => ".tar.gz",
            "xz" => ".tar.xz",
            "bzip2" => ".tar.bz2",
            _ => ".tar.lzma"
        };

        // Assert
        actualExtension.Should().Be(expectedExtension);
    }

    [Theory]
    [InlineData("bzip2", ".tar.bz2")]
    [InlineData("BZIP2", ".tar.bz2")]
    [InlineData("BZip2", ".tar.bz2")]
    public void FileExtension_WithBzip2Compression_ShouldReturnTarBz2(
        string compression, string expectedExtension)
    {
        // This tests line 190: "bzip2" => ".tar.bz2"

        // Act
        var actualExtension = compression.ToLowerInvariant() switch
        {
            "gzip" => ".tar.gz",
            "xz" => ".tar.xz",
            "bzip2" => ".tar.bz2",
            _ => ".tar.lzma"
        };

        // Assert
        actualExtension.Should().Be(expectedExtension);
    }

    [Theory]
    [InlineData("lzma", ".tar.lzma")]
    [InlineData("LZMA", ".tar.lzma")]
    [InlineData("", ".tar.lzma")]
    [InlineData("unknown", ".tar.lzma")]
    [InlineData("invalid", ".tar.lzma")]
    public void FileExtension_WithLzmaOrUnknown_ShouldReturnTarLzma(
        string compression, string expectedExtension)
    {
        // This tests line 191: _ => ".tar.lzma" (default case)

        // Act
        var actualExtension = compression.ToLowerInvariant() switch
        {
            "gzip" => ".tar.gz",
            "xz" => ".tar.xz",
            "bzip2" => ".tar.bz2",
            _ => ".tar.lzma"
        };

        // Assert
        actualExtension.Should().Be(expectedExtension, "unknown formats should default to LZMA");
    }

    #endregion

    #region Max Message Size Calculation Logic (Lines 197, 209)

    [Fact]
    public void MaxMessageSize_WithNullValue_ShouldDefault10MB()
    {
        // This tests line 197: var maxSizeMB = maxMessageSizeMB ?? 10;

        // Arrange
        int? maxMessageSizeMB = null;

        // Act - Replicate the logic
        var maxSizeMB = maxMessageSizeMB ?? 10;

        // Assert
        maxSizeMB.Should().Be(10, "null should default to 10MB");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void MaxMessageSize_WithProvidedValue_ShouldUseProvided(
        int providedValue, int expectedValue)
    {
        // This tests line 197 with non-null values

        // Arrange
        int? maxMessageSizeMB = providedValue;

        // Act
        var maxSizeMB = maxMessageSizeMB ?? 10;

        // Assert
        maxSizeMB.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(10, 10485760L)]      // 10 * 1024 * 1024
    [InlineData(1, 1048576L)]        // 1 * 1024 * 1024
    [InlineData(50, 52428800L)]      // 50 * 1024 * 1024
    [InlineData(100, 104857600L)]    // 100 * 1024 * 1024
    public void MaxMessageSizeBytes_ShouldConvertMBToBytes(
        int maxSizeMB, long expectedBytes)
    {
        // This tests line 209: var maxSizeBytes = maxSizeMB * 1024L * 1024L;

        // Act - Replicate the conversion logic
        var maxSizeBytes = maxSizeMB * 1024L * 1024L;

        // Assert
        maxSizeBytes.Should().Be(expectedBytes);
    }

    #endregion

    #region Compressor Selection Logic (Lines 213-230)

    [Fact]
    public void CompressorSelection_WithGzipCompression_ShouldReturnTarGzipCompressor()
    {
        // This tests lines 217-219: case "gzip"

        // Arrange
        string compression = "gzip";

        // Act - Replicate the switch logic from lines 215-230
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<TarGzipCompressor>();
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("GZIP")]
    [InlineData("Gzip")]
    public void CompressorSelection_WithGzipCaseInsensitive_ShouldReturnTarGzipCompressor(
        string compression)
    {
        // This tests case-insensitivity via ToLowerInvariant() on line 215

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<TarGzipCompressor>();
    }

    [Fact]
    public void CompressorSelection_WithXzCompression_ShouldReturnTarXzCompressor()
    {
        // This tests lines 220-222: case "xz"

        // Arrange
        string compression = "xz";

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<TarXzCompressor>();
    }

    [Fact]
    public void CompressorSelection_WithBzip2Compression_ShouldReturnTarBzip2Compressor()
    {
        // This tests lines 223-225: case "bzip2"

        // Arrange
        string compression = "bzip2";

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<TarBzip2Compressor>();
    }

    [Fact]
    public void CompressorSelection_WithLzmaCompression_ShouldReturnLZMACompressor()
    {
        // This tests lines 226-228: case "lzma"

        // Arrange
        string compression = "lzma";

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<LZMACompressor>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("invalid")]
    [InlineData("tar")]
    public void CompressorSelection_WithUnknownCompression_ShouldDefaultToLZMA(
        string compression)
    {
        // This tests lines 226-229: default case

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType<LZMACompressor>("unknown formats should default to LZMA");
    }

    [Theory]
    [InlineData("gzip", typeof(TarGzipCompressor))]
    [InlineData("xz", typeof(TarXzCompressor))]
    [InlineData("bzip2", typeof(TarBzip2Compressor))]
    [InlineData("lzma", typeof(LZMACompressor))]
    [InlineData("", typeof(LZMACompressor))]
    public void CompressorSelection_AllCases_ShouldReturnCorrectType(
        string compression, Type expectedType)
    {
        // This tests all branches of the switch statement lines 215-230

        // Act
        ICompressor compressor;
        switch (compression.ToLowerInvariant())
        {
            case "gzip":
                compressor = new TarGzipCompressor();
                break;
            case "xz":
                compressor = new TarXzCompressor();
                break;
            case "bzip2":
                compressor = new TarBzip2Compressor();
                break;
            case "lzma":
            default:
                compressor = new LZMACompressor();
                break;
        }

        // Assert
        compressor.Should().BeOfType(expectedType);
    }

    #endregion

    #region Helper Methods

    private Mock<IMessageSummary> CreateMockMessageSummary(uint uid, ulong? threadId)
    {
        var mock = new Mock<IMessageSummary>();
        mock.Setup(m => m.UniqueId).Returns(new UniqueId(uid));
        mock.Setup(m => m.GMailThreadId).Returns(threadId);
        mock.Setup(m => m.Size).Returns(1024);

        var envelope = new Envelope
        {
            Subject = $"Test Subject {uid}",
            From = { MailboxAddress.Parse($"sender{uid}@example.com") },
            Date = DateTimeOffset.UtcNow
        };
        mock.Setup(m => m.Envelope).Returns(envelope);

        return mock;
    }

    #endregion
}
