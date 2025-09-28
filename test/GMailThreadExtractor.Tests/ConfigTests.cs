using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GMailThreadExtractor;

namespace GMailThreadExtractor.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _tempFiles;

    public ConfigTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"config_tests_{Guid.NewGuid():N}");
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

    private string CreateTempConfigFile(object configData)
    {
        var path = Path.Combine(_testDirectory, $"config_{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task LoadFromFileAsync_WithValidConfig_ShouldLoadCorrectly()
    {
        // Arrange
        var configData = new
        {
            email = "test@example.com",
            password = "test-password",
            search = "from:important@example.com",
            label = "Important",
            output = "test-output",
            compression = "lzma",
            timeoutMinutes = 10,
            maxMessageSizeMB = 20
        };
        var configPath = CreateTempConfigFile(configData);

        // Act
        var config = await Config.LoadFromFileAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Email.Should().Be("test@example.com");
        config.Password.Should().Be("test-password");
        config.Search.Should().Be("from:important@example.com");
        config.Label.Should().Be("Important");
        config.Output.Should().Be("test-output");
        config.Compression.Should().Be("lzma");
        config.TimeoutMinutes.Should().Be(10);
        config.MaxMessageSizeMB.Should().Be(20);
    }

    [Fact]
    public async Task LoadFromFileAsync_WithNonexistentFile_ShouldReturnEmptyConfig()
    {
        // Arrange
        var nonexistentPath = Path.Combine(_testDirectory, "nonexistent.json");

        // Act
        var config = await Config.LoadFromFileAsync(nonexistentPath);

        // Assert
        config.Should().NotBeNull();
        config.Email.Should().BeNull();
        config.Password.Should().BeNull();
        config.Search.Should().BeNull();
        config.Output.Should().BeNull();
    }

    [Fact]
    public async Task LoadFromFileAsync_WithInvalidJson_ShouldThrow()
    {
        // Arrange
        var invalidJsonPath = Path.Combine(_testDirectory, "invalid.json");
        File.WriteAllText(invalidJsonPath, "{ invalid json content");
        _tempFiles.Add(invalidJsonPath);

        // Act & Assert
        var act = async () => await Config.LoadFromFileAsync(invalidJsonPath);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid configuration file*");
    }

    [Fact]
    public async Task LoadFromFileAsync_WithCommentsAndTrailingCommas_ShouldLoad()
    {
        // Arrange
        var jsonWithComments = """
        {
            // This is a comment
            "email": "test@example.com",
            "password": "test-password", // Another comment
            "search": "from:test@example.com",
            "compression": "gzip",
        }
        """;
        var configPath = Path.Combine(_testDirectory, "with_comments.json");
        File.WriteAllText(configPath, jsonWithComments);
        _tempFiles.Add(configPath);

        // Act
        var config = await Config.LoadFromFileAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Email.Should().Be("test@example.com");
        config.Compression.Should().Be("gzip");
    }

    [Fact]
    public void Validate_WithValidConfig_ShouldNotThrow()
    {
        // Arrange
        var config = new Config
        {
            Email = "valid@example.com",
            Password = "valid-password",
            Search = "from:test@example.com",
            Output = "valid-output",
            Compression = "lzma",
            TimeoutMinutes = 5,
            MaxMessageSizeMB = 10
        };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("lzma")]
    [InlineData("LZMA")]
    [InlineData("Lzma")]
    [InlineData("gzip")]
    [InlineData("GZIP")]
    [InlineData("Gzip")]
    public void Validate_WithValidCompressionCaseInsensitive_ShouldNotThrow(string compression)
    {
        // Arrange
        var config = new Config { Compression = compression };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("invalid-email", "Email must be a valid email address format.")]
    [InlineData("", "Email must be a valid email address format.")]
    [InlineData("no-at-symbol", "Email must be a valid email address format.")]
    public void Validate_WithInvalidEmail_ShouldThrow(string email, string expectedMessage)
    {
        // Arrange
        var config = new Config { Email = email };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("invalid", "Compression method must be one of: lzma, gzip")]
    [InlineData("zip", "Compression method must be one of: lzma, gzip")]
    public void Validate_WithInvalidCompression_ShouldThrow(string compression, string expectedMessage)
    {
        // Arrange
        var config = new Config { Compression = compression };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData(0, "Timeout must be between 1 and 60 minutes.")]
    [InlineData(-1, "Timeout must be between 1 and 60 minutes.")]
    [InlineData(61, "Timeout must be between 1 and 60 minutes.")]
    public void Validate_WithInvalidTimeout_ShouldThrow(int timeout, string expectedMessage)
    {
        // Arrange
        var config = new Config { TimeoutMinutes = timeout };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData(0, "Max message size must be between 1 and 1000 MB.")]
    [InlineData(-1, "Max message size must be between 1 and 1000 MB.")]
    [InlineData(1001, "Max message size must be between 1 and 1000 MB.")]
    public void Validate_WithInvalidMaxMessageSize_ShouldThrow(int maxSize, string expectedMessage)
    {
        // Arrange
        var config = new Config { MaxMessageSizeMB = maxSize };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Fact]
    public void Validate_WithInvalidOutputPath_ShouldThrow()
    {
        // Arrange
        var config = new Config { Output = "invalid<>path" };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
    }

    [Fact]
    public void Validate_WithLongSearchQuery_ShouldThrow()
    {
        // Arrange
        var longSearch = new string('a', 1001);
        var config = new Config { Search = longSearch };

        // Act & Assert
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>().WithMessage("*too long*");
    }

    [Fact]
    public void MergeWithCommandLine_ShouldPrioritizeCommandLineValues()
    {
        // Arrange
        var fileConfig = new Config
        {
            Email = "file@example.com",
            Password = "file-password",
            Search = "file-search",
            Compression = "gzip",
            TimeoutMinutes = 5
        };

        // Act
        var merged = fileConfig.MergeWithCommandLine(
            cmdEmail: "cmd@example.com",
            cmdPassword: null, // Should use file value
            cmdSearch: "cmd-search",
            cmdLabel: "cmd-label",
            cmdOutput: "cmd-output",
            cmdCompression: null, // Should use file value
            cmdTimeoutMinutes: 10);

        // Assert
        merged.Email.Should().Be("cmd@example.com"); // Command line wins
        merged.Password.Should().Be("file-password"); // File value used
        merged.Search.Should().Be("cmd-search"); // Command line wins
        merged.Label.Should().Be("cmd-label"); // Command line value
        merged.Output.Should().Be("cmd-output"); // Command line value
        merged.Compression.Should().Be("gzip"); // File value used
        merged.TimeoutMinutes.Should().Be(10); // Command line wins
    }

    [Fact]
    public void MergeWithCommandLine_WithEmptyCommandLineStrings_ShouldUseFileValues()
    {
        // Arrange
        var fileConfig = new Config
        {
            Email = "file@example.com",
            Search = "file-search",
            Compression = "lzma"
        };

        // Act
        var merged = fileConfig.MergeWithCommandLine(
            cmdEmail: "", // Empty string should use file value
            cmdPassword: "   ", // Whitespace should use file value
            cmdSearch: "cmd-search",
            cmdLabel: null,
            cmdOutput: "cmd-output",
            cmdCompression: "", // Empty should use file value
            cmdTimeoutMinutes: null);

        // Assert
        merged.Email.Should().Be("file@example.com"); // File value used (empty cmd)
        merged.Password.Should().BeNull(); // File had no value
        merged.Search.Should().Be("cmd-search"); // Command line value
        merged.Compression.Should().Be("lzma"); // File value used (empty cmd)
    }

    [Fact]
    public void MergeWithCommandLine_ShouldValidateMergedResult()
    {
        // Arrange
        var fileConfig = new Config
        {
            Email = "valid@example.com",
            TimeoutMinutes = 5
        };

        // Act & Assert - Should validate the merged result
        var act = () => fileConfig.MergeWithCommandLine(
            cmdEmail: null,
            cmdPassword: null,
            cmdSearch: null,
            cmdLabel: null,
            cmdOutput: null,
            cmdCompression: "invalid-compression", // This should cause validation to fail
            cmdTimeoutMinutes: null);

        act.Should().Throw<ArgumentException>().WithMessage("*Compression method*");
    }

}