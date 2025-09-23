# Repository Guidelines

## Project Structure & Module Organization
The solution pivots around `gmail-thread-extractor.sln`, which loads the CLI project in `src/GMailThreadExtractor`. `Program.cs` composes the System.CommandLine entrypoint, while `Config.cs` merges command-line overrides with JSON defaults. Compression helpers live in `src/ArchivalSupport` (`LZMACompressor`, `TarGzipCompressor`, `MessageWriter`), so add new archive logic there. Use `config.example.json` as the reference template, and reserve `test/` for unit-test projects.

## Build, Test, and Development Commands
Run `dotnet restore` once per environment to hydrate NuGet packages. Use `dotnet build --configuration Release` for reproducible binaries; stick with Debug while iterating. Exercise the CLI via `dotnet run --project src/GMailThreadExtractor -- --config config.json` or by passing `--search "from:foo" --output threads.tar.lzma`. When tests arrive, gate changes with `dotnet test` at the solution root.

## Coding Style & Naming Conventions
Follow .NET defaults: four-space indentation, braces on new lines for namespaces, types, and methods, and use implicit `var` only when the type is obvious. Favor `PascalCase` for classes and public members, `camelCase` for locals and parameters, and suffix async methods with `Async`. Run `dotnet format` before submitting to keep StyleCop conventions and `using` order consistent.

## Testing Guidelines
Target xUnit for new test projects (e.g., `test/GMailThreadExtractor.Tests`). Mirror the namespace under test, and name files `<TargetClass>Tests.cs`. Cover edge cases such as missing config files, invalid compression flags, and IMAP failures. Keep tests hermetic: stub network interactions and write output to `Path.GetTempPath()` so archives never touch the repo.

## Commit & Pull Request Guidelines
Use imperative, capitalized summaries (`Add config file support`). Group related changes into a single commit whenever feasible. Pull requests should describe the scenario, list validation steps (`dotnet build`, `dotnet run`), and link any tracking issues. Attach terminal output or sample archives when fixing bugs so reviewers can replay the workflow.

## Configuration & Security Notes
Never commit real Gmail credentials or archives. Copy `config.example.json` to `config.json` locally and add new keys to both files when introducing options. If a change alters credential handling, call it out in the PR and update follow-up steps in `README.md`.
