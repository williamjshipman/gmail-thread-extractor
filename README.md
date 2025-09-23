# gmail-thread-extractor

A tool for extracting and saving an entire thread of messages from GMail

## Description

This command line tool allows you to download all email threads that match a given search term and/or label from your GMail account. It uses the IMAP protocol to connect to your GMail account and download the messages. The messages are saved as .eml files in a compressed archive (tar.lzma or tar.gz). Each thread is saved in a separate folder in the archive.

I created this tool to help me to download important email threads that were taking up too much space in my GMail account, so I could archive them somewhere and delete them in GMail.

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
dotnet run --project .\src\GMailThreadExtractor\ --email <email> --password <app password> --search "<search terms>" --output <output file> --compression <lzma|gzip>
```

Replace `<email>` with your GMail email address, `<app password>` with the app password you generated, `<search terms>` with the search terms you want to use to find the thread, and `<output file>` with the path to the file where you want to save the threads that were found.

**Arguments:**
- `--email` and `--password` are optional (you will be prompted if not provided)
- `--search` and `--output` are required
- `--compression` is optional (defaults to "lzma") - choose "lzma" for .tar.lzma or "gzip" for .tar.gz
- `--config` is optional - specify a JSON configuration file path
- `--label` is optional - filter by Gmail label

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
  "compression": "lzma"
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

As of now, the tool has the following small set of features:

:white_check_mark: Download all email threads that match a given search term and/or label from your GMail account.\
:white_check_mark: Save the messages as .eml files in compressed archives (tar.lzma or tar.gz).\
:white_check_mark: Multiple compression options: LZMA (7zip) or Gzip compression.\
:white_check_mark: Each thread is saved in a separate folder in the archive.\
:white_check_mark: JSON configuration file support for default settings.\
:white_check_mark: Command-line options take precedence over configuration files.

## Roadmap

The following features and modifications are planned for the future:

- [x] Add support for tar.gz compression format.
- [ ] Add support for tar.xz format.
- [ ] Get proper .7z support working, at the moment the 7zip SDK is used to create tar.lzma files.
- [ ] Add support for OAuth2 authentication, although a low priority since the config is a pain.
- [x] Clean up the code, splitting it into smaller classes.
- [ ] Add unit tests.
- [ ] CI/CD pipeline using GitHub Actions - one day...

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Project structure

The project is split into src and test folders. The src folder contains the source code for the project, and the test folder contains the unit tests for the project. At the moment there are no unit tests, but they will be added in the future.
The src folder contains the following subfolders:

- GMailThreadExtractor: The main executable project folder, containing the source code for the project.
- ArchivalSupport: Contains classes for compressing and archiving email threads.

## Contributing

Contributions are welcome, please fork the repository and create a pull request. Please log issues for bugs, suggestions, feature requests, etc. However, given my limited time to work on side projects, I make no guarantees that I will respond to issues in a timely manner.
