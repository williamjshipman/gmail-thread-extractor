# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 9.0 command-line tool for extracting Gmail email threads using IMAP. It downloads complete email threads based on search queries or labels and saves them as compressed tar.lzma archives using 7zip LZMA compression.

## Development Commands

### Building

```bash
dotnet build
```

### Running the application

```bash
dotnet run --project .\src\GMailThreadExtractor\ --email <email> --password <app_password> --search "<search_terms>" --output <output_file> --compression <lzma|gzip>
```

The `--email` and `--password` parameters are optional (user will be prompted if not provided).
The `--search` and `--output` parameters are required.
The `--compression` parameter is optional (defaults to "lzma").

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
  "compression": "lzma"
}
```

The `compression` field accepts either "lzma" (default) or "gzip". The appropriate file extension (.tar.lzma or .tar.gz) will be automatically appended to the output filename if not already present.

### Solution structure

- Build the entire solution: `dotnet build gmail-thread-extractor.sln`
- Clean: `dotnet clean`

## Architecture

### Project Structure

The solution contains two main projects:

1. **GMailThreadExtractor** (`src/GMailThreadExtractor/`) - Main executable project

   - `Program.cs` - Entry point with command-line argument parsing using System.CommandLine
   - `GMailThreadExtractor.cs` - Core IMAP logic for connecting to Gmail and extracting threads
   - `ArgumentParser.cs` - Currently empty, command-line parsing is handled in Program.cs

2. **ArchivalSupport** (`src/ArchivalSupport/`) - Library for compression and archival
   - `ICompressor.cs` - Interface defining the compression contract for all compressor implementations
   - `BaseCompressor.cs` - Static methods for writing email threads to tar archives
   - `LZMACompressor.cs` - LZMA compression implementation using SevenZip LZMA encoder (implements ICompressor)
   - `TarGzipCompressor.cs` - Gzip compression implementation using SharpZipLib (implements ICompressor)
   - `MessageWriter.cs` - Converts MailKit messages to MessageBlob objects for serialization

### Key Dependencies

- **MailKit** - IMAP client for Gmail connectivity
- **MimeKit** - Email message parsing and handling
- **System.CommandLine** (beta) - Command-line argument parsing
- **LZMA-SDK** - 7zip LZMA compression
- **SharpZipLib** - Tar archive creation

### Data Flow

1. Command-line arguments parsed in `Program.cs`
2. `GMailThreadExtractor` connects to Gmail via IMAP
3. Searches for emails using Gmail search queries and/or labels
4. Groups messages by Gmail thread ID
5. Downloads full message content and converts to `MessageBlob` objects
6. Selected compressor (LZMA or Gzip) creates tar archive and applies compression
7. Output saved as `.tar.lzma` or `.tar.gz` file (based on compression setting)

### Important Notes

- Uses Gmail app passwords (not regular password) for authentication
- Searches the "All Mail" folder to ensure complete thread retrieval
- Each thread is stored in a separate folder within the tar archive
- File naming convention: `{sender}_{date}.eml` for individual messages
- LZMA compression uses 256MB dictionary size and BT4 match finder for optimal compression
- Gzip compression uses level 9 (best compression) for tar.gz output format
- Compression method can be selected via `--compression` option or config file (defaults to "lzma")
- File extensions are automatically appended based on compression method

## Testing

Currently no unit tests exist (planned for future implementation).
