using System;
using System.IO;
using System.Threading.Tasks;
using MailKit;
using MimeKit;
using Moq;
using FluentAssertions;
using ArchivalSupport;

namespace ArchivalSupport.Tests;

public class MessageWriterTests
{
    private readonly Mock<IMessageSummary> _mockMessageSummary;
    private readonly MimeMessage _testMessage;

    public MessageWriterTests()
    {
        _mockMessageSummary = new Mock<IMessageSummary>();
        _mockMessageSummary.Setup(x => x.UniqueId).Returns(new UniqueId(12345));
        _mockMessageSummary.Setup(x => x.Size).Returns(1024);

        _testMessage = new MimeMessage();
        _testMessage.From.Add(new MailboxAddress("Test Sender", "sender@example.com"));
        _testMessage.To.Add(new MailboxAddress("Test Recipient", "recipient@example.com"));
        _testMessage.Subject = "Test Subject";
        _testMessage.Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        _testMessage.Body = new TextPart("plain") { Text = "Test message body" };
    }

    [Fact]
    public void MessageToBlob_WithSmallKnownSize_ShouldReturnInMemoryBlob()
    {
        // Arrange
        var maxSize = 10 * 1024 * 1024; // 10MB
        _mockMessageSummary.Setup(x => x.Size).Returns(1024); // 1KB

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage, maxSize);

        // Assert
        result.Should().NotBeNull();
        result.IsStreaming.Should().BeFalse();
        result.Blob.Should().NotBeNull();
        result.Blob!.Length.Should().BeGreaterThan(0);
        result.Size.Should().Be(result.Blob.Length);
        result.UniqueId.Should().Be("12345");
        result.Subject.Should().Be("Test Subject");
        result.From.Should().Contain("sender@example.com");
        result.To.Should().Contain("recipient@example.com");
        result.Date.Should().Be(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MessageToBlob_WithLargeKnownSize_ShouldReturnStreamingBlob()
    {
        // Arrange
        var maxSize = 1024; // 1KB
        _mockMessageSummary.Setup(x => x.Size).Returns((uint)(10 * 1024 * 1024)); // 10MB

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage, maxSize);

        // Assert
        result.Should().NotBeNull();
        result.IsStreaming.Should().BeTrue();
        result.Blob.Should().BeNull();
        result.StreamFunc.Should().NotBeNull();
        result.Size.Should().Be(10 * 1024 * 1024);
        result.UniqueId.Should().Be("12345");
    }

    [Fact]
    public void MessageToBlob_WithUnknownSize_ShouldReturnStreamingBlob()
    {
        // Arrange
        var maxSize = 10 * 1024 * 1024; // 10MB
        _mockMessageSummary.Setup(x => x.Size).Returns((uint?)null); // Unknown size

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage, maxSize);

        // Assert
        result.Should().NotBeNull();
        result.IsStreaming.Should().BeTrue();
        result.Blob.Should().BeNull();
        result.StreamFunc.Should().NotBeNull();
        result.Size.Should().Be(-1); // Unknown size indicator
        result.UniqueId.Should().Be("12345");
    }

    [Fact]
    public async Task MessageToBlob_StreamingBlob_ShouldWriteCorrectData()
    {
        // Arrange
        var maxSize = 1024; // Small size to force streaming
        _mockMessageSummary.Setup(x => x.Size).Returns((uint)(10 * 1024 * 1024)); // Large size

        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage, maxSize);

        // Act
        using var outputStream = new MemoryStream();
        await result.StreamFunc!(outputStream);

        // Assert
        outputStream.Length.Should().BeGreaterThan(0);
        var streamedData = outputStream.ToArray();

        // Verify the streamed data is valid by parsing it back
        using var inputStream = new MemoryStream(streamedData);
        var parsedMessage = await MimeMessage.LoadAsync(inputStream);

        parsedMessage.Subject.Should().Be(_testMessage.Subject);
        parsedMessage.From.ToString().Should().Be(_testMessage.From.ToString());
        parsedMessage.To.ToString().Should().Be(_testMessage.To.ToString());
    }

    [Fact]
    public void MessageToBlob_WithNullMessageSummary_ShouldHandleGracefully()
    {
        // Act
        var result = MessageWriter.MessageToBlob(null!, _testMessage);

        // Assert
        result.Should().NotBeNull();
        result.UniqueId.Should().Be("unknown");
        result.IsStreaming.Should().BeTrue(); // Unknown size defaults to streaming
    }

    [Fact]
    public void MessageToBlob_WithEmptyMessage_ShouldCreateValidBlob()
    {
        // Arrange
        var emptyMessage = new MimeMessage();
        _mockMessageSummary.Setup(x => x.Size).Returns(100);

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, emptyMessage);

        // Assert
        result.Should().NotBeNull();
        result.Subject.Should().Be(string.Empty);
        result.From.Should().Be(string.Empty);
        result.To.Should().Be(string.Empty);
        result.Blob.Should().NotBeNull();
        result.Blob!.Length.Should().BeGreaterThan(0); // Even empty messages have headers
    }

    [Fact]
    public void MessageToBlob_FileNameGeneration_ShouldBeValid()
    {
        // Arrange
        _mockMessageSummary.Setup(x => x.Size).Returns(1024);

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage);

        // Assert
        result.FileName.Should().NotBeNullOrEmpty();
        result.FileName.Should().EndWith(".eml");
        result.FileName.Should().Contain("12345"); // UniqueId
        result.FileName.Should().Contain("2024-01-15"); // Date

        // Verify no invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        result.FileName.Should().NotContainAny(invalidChars.Select(c => c.ToString()));
    }

    [Fact]
    public void MessageToBlob_DateHandling_ShouldConvertToUtc()
    {
        // Arrange
        var localDate = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5)); // UTC+5
        _testMessage.Date = localDate;
        _mockMessageSummary.Setup(x => x.Size).Returns(1024);

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage);

        // Assert
        result.Date.Kind.Should().Be(DateTimeKind.Utc);
        result.Date.Should().Be(new DateTime(2024, 6, 15, 9, 30, 0, DateTimeKind.Utc)); // Converted to UTC
    }

    [Theory]
    [InlineData(1024, 1024, false)] // Exactly at threshold
    [InlineData(1023, 1024, false)] // Just under threshold
    [InlineData(1025, 1024, true)]  // Just over threshold
    [InlineData(10 * 1024 * 1024, 1024, true)] // Much larger than threshold
    public void MessageToBlob_SizeThresholds_ShouldRespectBoundaries(uint messageSize, long maxSizeBytes, bool expectedStreaming)
    {
        // Arrange
        _mockMessageSummary.Setup(x => x.Size).Returns(messageSize);

        // Act
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage, maxSizeBytes);

        // Assert
        result.IsStreaming.Should().Be(expectedStreaming);

        if (expectedStreaming)
        {
            result.Blob.Should().BeNull();
            result.StreamFunc.Should().NotBeNull();
        }
        else
        {
            result.Blob.Should().NotBeNull();
            result.StreamFunc.Should().BeNull();
        }
    }

    [Fact]
    public void MessagesToBlobs_WithMultipleMessages_ShouldProcessAll()
    {
        // Arrange
        var messages = new List<MimeMessage> { _testMessage, _testMessage };
        var summaries = new List<IMessageSummary>
        {
            _mockMessageSummary.Object,
            _mockMessageSummary.Object
        };

        // Act
        var results = MessageWriter.MessagesToBlobs(messages, summaries);

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(blob =>
        {
            blob.Should().NotBeNull();
            blob.Subject.Should().Be("Test Subject");
        });
    }

    [Fact]
    public void MessagesToBlobs_WithMismatchedCounts_ShouldThrow()
    {
        // Arrange
        var messages = new List<MimeMessage> { _testMessage };
        var summaries = new List<IMessageSummary>
        {
            _mockMessageSummary.Object,
            _mockMessageSummary.Object
        };

        // Act & Assert
        var act = () => MessageWriter.MessagesToBlobs(messages, summaries);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MessageBlob_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var result = MessageWriter.MessageToBlob(_mockMessageSummary.Object, _testMessage);

        // Act
        var stringResult = result.ToString();

        // Assert
        stringResult.Should().Contain("Subject: Test Subject");
        stringResult.Should().Contain("From: ");
        stringResult.Should().Contain("sender@example.com");
        stringResult.Should().Contain("To: ");
        stringResult.Should().Contain("recipient@example.com");
        stringResult.Should().Contain("UID: 12345");
        stringResult.Should().Contain("Size: ");
    }
}