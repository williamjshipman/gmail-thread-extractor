# gmail-thread-extractor

A tool for extracting and saving an entire thread of messages from GMail

## Description

This robust command line tool downloads complete email threads that match search terms and/or labels from your GMail account. It uses IMAP protocols with comprehensive error handling and retry logic to reliably extract messages. The messages are saved as .eml files in compressed archives (tar.lzma or tar.gz) with secure file permissions. Each thread is organized in separate folders within the archive.

**Key Features:**
- **Secure file handling**: Platform-specific file permissions (Windows ACLs, Unix 600)
- **Reliable**: Exponential backoff retry logic for network failures
- **Memory-efficient**: Streaming support for large emails
- **Cross-platform**: Works on Windows, macOS, and Linux
- **Configurable**: JSON configuration with validation
- **Well-tested**: 120+ unit and integration tests

I created this tool to help archive important email threads that were taking up space in GMail, allowing for safe local storage and deletion from the online account.

## Prerequisites

- .NET 9.0 SDK or later for building the project.
- .NET 9.0 Runtime or later for running the project.
- A GMail account with app passwords enabled. This app relies on IMAP to access your GMail account. You can enable app passwords by following the instructions [here](https://myaccount.google.com/apppasswords). **Make sure to save the app password somewhere secure, you cannot retrieve it again from GMail.**

## Building the project

1. Clone the repository:

```bash
git clone https://github.com/williamjshipman/gmail-thread-extractor.git
cd gmail-thread-extractor
```

2. .NET Build

```bash
dotnet build
```

## Running the code

1. Open a terminal and navigate to the project directory.

```bash
cd gmail-thread-extractor
```

2. Run the project with the following command:

```bash
dotnet run --project .\src\GMailThreadExtractor\ --email <email> --password <app password> --search "<search terms>" --output <output file> --compression <lzma|gzip> --timeout <minutes> --max-message-size <MB>
```

Replace `<email>` with your GMail email address, `<app password>` with the app password you generated, `<search terms>` with the search terms you want to use to find the thread, and `<output file>` with the path to the file where you want to save the threads that were found.

**Arguments:**
- `--email` and `--password` are optional (you will be prompted if not provided)
- `--search` and `--output` are required
- `--compression` is optional (defaults to "lzma", case-insensitive) - choose "lzma" for .tar.lzma or "gzip" for .tar.gz
- `--config` is optional - specify a JSON configuration file path
- `--label` is optional - filter by Gmail label
- `--timeout` is optional (defaults to 5 minutes, range: 1-60 minutes) - IMAP operation timeout
- `--max-message-size` is optional (defaults to 10 MB, range: 1-1000 MB) - streaming threshold for large messages

## Testing

The project includes comprehensive unit and integration tests covering all major functionality:

```bash
# Run all tests (120+ tests)
dotnet test

# Run specific test project
dotnet test test/GMailThreadExtractor.Tests/
dotnet test test/ArchivalSupport.Tests/

# Run with verbose output
dotnet test --verbosity normal
```

**Test Coverage:**
- **120 total tests** with 100% pass rate
- Configuration validation and error handling
- Retry logic with exponential backoff
- Cross-platform secure file operations
- Compression algorithms (LZMA, Gzip)
- Memory management and streaming
- Error categorization and recovery strategies
- Integration workflows with mock data

All tests are hermetic (no live network connections or filesystem writes outside temp locations).

## Configuration File

You can use a JSON configuration file to provide default values for command-line arguments. Command-line arguments will override config file values.

**Create a config file (e.g., `config.json`):**

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

**Use the config file:**

```bash
dotnet run --project .\src\GMailThreadExtractor\ --config config.json
```

**Default config file locations** (checked automatically if no `--config` specified):
1. `config.json` (current directory)
2. `gmail-extractor.json` (current directory)
3. `.gmail-extractor.json` (user home directory)

## Features

:white_check_mark: Download all email threads that match search terms and/or labels from your GMail account\
:white_check_mark: Save messages as .eml files in compressed archives (tar.lzma or tar.gz)\
:white_check_mark: Multiple compression options: LZMA (64MB dictionary) or Gzip (level 9) compression\
:white_check_mark: Each thread organized in separate folders within the archive\
:white_check_mark: JSON configuration file support with validation and comments support\
:white_check_mark: Command-line options take precedence over configuration files\
:white_check_mark: Sanitized archive names remain portable across operating systems\
:white_check_mark: **Secure file handling**: Platform-specific permissions (Windows ACLs, Unix 600)\
:white_check_mark: **Reliability**: Exponential backoff retry logic for network failures\
:white_check_mark: **Memory efficiency**: Streaming support for large emails (configurable threshold)\
:white_check_mark: **Cross-platform**: Works on Windows, macOS, and Linux\
:white_check_mark: **Comprehensive testing**: 120+ unit and integration tests\
:white_check_mark: **Error handling**: Structured error categorization and recovery strategies

## Roadmap

The following features and modifications are planned for the future:

- [x] Add support for tar.gz compression format
- [x] Clean up the code, splitting it into smaller classes
- [x] Add comprehensive unit and integration tests (120+ tests)
- [x] Add secure file handling with platform-specific permissions
- [x] Add retry logic with exponential backoff for reliability
- [x] Add streaming support for memory-efficient large message handling
- [x] Add structured error handling and categorization
- [ ] Add support for tar.xz format
- [ ] Get proper .7z support working (currently using 7zip SDK for tar.lzma)
- [ ] Add support for OAuth2 authentication (low priority due to configuration complexity)
- [ ] CI/CD pipeline using GitHub Actions

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Project structure

The project is organized into `src/` and `test/` folders with a clean separation of concerns:

**Source Projects:**
- `src/GMailThreadExtractor`: Main executable with CLI, configuration, retry logic, and IMAP operations
- `src/ArchivalSupport`: Compression, archival, and message processing utilities
- `src/Shared`: Common infrastructure (logging, error handling, secure file operations)

**Test Projects:**
- `test/GMailThreadExtractor.Tests`: Comprehensive tests for main application components (93 tests)
- `test/ArchivalSupport.Tests`: Tests for compression and archival functionality (27 tests)

**Key Components:**
- Configuration loading and validation with JSON support
- Platform-specific secure file operations (Windows ACLs, Unix permissions)
- Retry logic with exponential backoff for network reliability
- Memory-efficient streaming for large email messages
- Structured error handling with categorization
- Safe filename generation for cross-platform compatibility

## Contributing

Contributions are welcome, please fork the repository and create a pull request. Please log issues for bugs, suggestions, feature requests, etc. However, given my limited time to work on side projects, I make no guarantees that I will respond to issues in a timely manner.
