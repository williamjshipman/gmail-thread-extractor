# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 9.0 command-line tool for extracting Gmail email threads using IMAP. It downloads complete email threads based on search queries or labels and saves them as compressed archives using LZMA, XZ, or Gzip compression formats.

## Development Commands

### Building

```bash
dotnet build
```

### Running the application

```bash
dotnet run --project .\src\GMailThreadExtractor\ --email <email> --password <app_password> --search "<search_terms>" --output <output_file> --compression <lzma|xz|gzip> --timeout <minutes> --max-message-size <MB>
```

**Parameters:**
- `--email` and `--password` - Optional (user will be prompted if not provided)
- `--search` and `--output` - Required
- `--compression` - Optional (defaults to "lzma", case-insensitive: accepts "lzma", "LZMA", "xz", "XZ", "gzip", "GZIP", etc.)
- `--timeout` - Optional (defaults to 5 minutes, range: 1-60 minutes)
- `--max-message-size` - Optional (defaults to 10 MB, range: 1-1000 MB)

### Configuration File Support

The application supports JSON configuration files to provide default values for command-line arguments. Command-line arguments take precedence over config file values.

```bash
# Using a specific config file
dotnet run --project .\src\GMailThreadExtractor\GMailThreadExtractor.csproj --config path/to/config.json

# Using default config file locations (checked in order):
# 1. config.json (in current directory)
# 2. gmail-extractor.json (in current directory)
# 3. .gmail-extractor.json (in user home directory)
dotnet run --project .\src\GMailThreadExtractor\GMailThreadExtractor.csproj
```

Example config file format:

```json
{
  "email": "your-email@gmail.com",
  "password": "your-app-password",
  "search": "from:important-sender@example.com",
  "label": "Important",
  "output": "extracted-emails",
  "compression": "lzma",
  "timeoutMinutes": 5,
  "maxMessageSizeMB": 10
}
```

**Configuration Fields:**
- `compression` - Case-insensitive, accepts "lzma", "LZMA", "xz", "XZ", "gzip", "GZIP", etc. (defaults to "lzma")
- `timeoutMinutes` - IMAP operation timeout (1-60 minutes, defaults to 5) to prevent hanging on slow networks
- `maxMessageSizeMB` - Streaming threshold for large messages (1-1000 MB, defaults to 10) to optimize memory usage
- `email` - Gmail address (validated for proper email format)
- `password` - Gmail app password (not regular password)
- `search` - Gmail search query (max 1000 characters)
- `label` - Gmail label to filter by
- `output` - Output file path (validated for directory existence and filename safety)

The appropriate file extension (.tar.lzma, .tar.xz, or .tar.gz) will be automatically appended to the output filename if not already present. Configuration supports JSON comments and trailing commas for convenience.

### Solution structure

- Build the entire solution: `dotnet build gmail-thread-extractor.sln`
- Clean: `dotnet clean`

## Architecture

### Project Structure

The solution contains three main projects:

1. **GMailThreadExtractor** (`src/GMailThreadExtractor/`) - Main executable project

   - `Program.cs` - Entry point with command-line argument parsing using System.CommandLine
   - `GMailThreadExtractor.cs` - Core IMAP logic for connecting to Gmail and extracting threads
   - `Config.cs` - Configuration file loading and validation with JSON support
   - `RetryHelper.cs` - Retry logic with exponential backoff for handling transient failures

2. **ArchivalSupport** (`src/ArchivalSupport/`) - Library for compression and archival
   - `ICompressor.cs` - Interface defining the compression contract for all compressor implementations
   - `BaseCompressor.cs` - Static methods for writing email threads to tar archives
   - `LZMACompressor.cs` - LZMA compression implementation using SevenZip LZMA encoder (implements ICompressor)
   - `TarXzCompressor.cs` - XZ compression implementation using Joveler.Compression.XZ with true XZ format support (implements ICompressor)
   - `TarGzipCompressor.cs` - Gzip compression implementation using SharpZipLib (implements ICompressor)
   - `MessageWriter.cs` - Converts MailKit messages to MessageBlob objects for serialization
   - `SafeNameBuilder.cs` - Utilities for creating safe file and directory names for tar archives

3. **Shared** (`src/Shared/`) - Common utilities and infrastructure
   - `LoggingConfiguration.cs` - Centralized Serilog logging configuration
   - `ErrorHandling.cs` - Structured error handling with categorization and strategies
   - `SecureIOUtilities.cs` - Platform-specific secure file operations with ACLs/permissions

### Key Dependencies

- **MailKit** - IMAP client for Gmail connectivity
- **MimeKit** - Email message parsing and handling
- **System.CommandLine** (beta) - Command-line argument parsing
- **LZMA-SDK** - 7zip LZMA compression
- **Joveler.Compression.XZ** - True XZ format compression with LZMA2 algorithm
- **SharpZipLib** - Tar archive creation and Gzip compression
- **Serilog** - Structured logging with console and file output support

### Data Flow

1. Command-line arguments parsed in `Program.cs`
2. `GMailThreadExtractor` connects to Gmail via IMAP
3. Searches for emails using Gmail search queries and/or labels
4. Groups messages by Gmail thread ID
5. Downloads full message content and converts to `MessageBlob` objects
6. Selected compressor (LZMA, XZ, or Gzip) creates tar archive and applies compression
7. Output saved as `.tar.lzma`, `.tar.xz`, or `.tar.gz` file (based on compression setting)

### Security Features

- **Secure temporary files**: Platform-specific secure permissions (Windows ACLs restrict to current user, Unix 600 permissions)
- **Input validation**: Configuration fields are validated for safety (email format, file paths, size limits)
- **Safe file naming**: Automatic sanitization of email subjects and sender names for filesystem compatibility
- **Error handling**: Comprehensive structured error handling with categorization and logging

### Reliability Features

- **Retry logic**: Exponential backoff for transient network failures (SocketException, TimeoutException, etc.)
- **Error categorization**: Distinguishes between retryable (network, temporary) and non-retryable (authentication, configuration) errors
- **Graceful degradation**: Continues processing when individual messages fail
- **Memory management**: Automatic streaming for large messages to prevent memory exhaustion
- **Progress logging**: Detailed Serilog-based logging for monitoring and debugging

### Important Notes

- Uses Gmail app passwords (not regular password) for authentication
- Searches the "All Mail" folder to ensure complete thread retrieval
- Each thread is stored in a separate folder within the tar archive
- File naming convention: `{uniqueId}_{subject}_{timestamp}_{sender}.eml` for individual messages
- LZMA compression uses 64MB dictionary size and BT4 match finder for optimal compression
- XZ compression uses LZMA2 algorithm with Level 9 compression for maximum efficiency and true XZ format compliance
- Gzip compression uses level 9 (best compression) for tar.gz output format
- Compression method selection is case-insensitive via `--compression` option or config file
- File extensions are automatically appended based on compression method
- Thread directory names are limited to 100 characters for TAR compatibility

## Testing

The solution includes comprehensive unit and integration tests:

### Test Projects

- **ArchivalSupport.Tests** (`test/ArchivalSupport.Tests/`) - Tests for archival and compression components
  - `CompressionTests.cs` - LZMA, XZ, and Gzip compression algorithm tests with native library platform detection
  - `MessageWriterTests.cs` - Email message serialization and blob creation tests

- **GMailThreadExtractor.Tests** (`test/GMailThreadExtractor.Tests/`) - Tests for main application components
  - `ConfigTests.cs` - Configuration loading, validation, and merging tests
  - `RetryHelperTests.cs` - Retry logic with exponential backoff tests
  - `ErrorHandlingTests.cs` - Structured error handling and categorization tests
  - `IntegrationTests.cs` - End-to-end workflow and compression integration tests
  - `SecureIOUtilitiesTests.cs` - Cross-platform secure file operations tests

### Test Coverage

- **126 total tests** with 100% pass rate
- Configuration validation (email format, compression options, file paths)
- Error handling strategies (LogAndContinue, LogAndThrow, LogAndTerminate)
- Retry logic for various exception types (network, timeout, authentication)
- Compression algorithms (LZMA, XZ, Gzip) with realistic email data and large content validation
- XZ native library platform detection and initialization for Windows/Linux/macOS
- Secure file operations (Windows ACLs, Unix permissions)
- Memory management (streaming vs in-memory message processing)
- Safe filename generation and filesystem compatibility

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test test/GMailThreadExtractor.Tests/

# Run with verbose output
dotnet test --verbosity normal
```

### Test Categories

- **Unit Tests**: Individual component functionality
- **Integration Tests**: End-to-end workflows with mock data
- **Cross-platform Tests**: Windows and Unix-specific behaviors
- **Error Handling Tests**: Exception scenarios and recovery strategies
