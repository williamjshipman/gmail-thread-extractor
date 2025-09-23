# Repository Guidelines

## Project Structure & Module Organization
The solution pivots around `gmail-thread-extractor.sln`, which loads the CLI project in `src/GMailThreadExtractor` and the compression helpers in `src/ArchivalSupport`. `Program.cs` composes the System.CommandLine entrypoint, while `Config.cs` merges command-line overrides with JSON defaults. Path sanitization and compressor implementations live under `ArchivalSupport`. The `test/ArchivalSupport.Tests` project exercises those helpers—add additional suites alongside it and keep configuration samples in `config.example.json`.

## Build, Test, and Development Commands
Run `dotnet restore` once per environment to hydrate NuGet packages. Use `dotnet build --configuration Release` for reproducible binaries; stick with Debug while iterating. Exercise the CLI via `dotnet run --project src/GMailThreadExtractor -- --config config.json` or by passing `--search "from:foo" --output threads.tar.lzma`. Gate changes with `dotnet test` from the repo root so the xUnit suite stays green.

## Coding Style & Naming Conventions
Follow .NET defaults: four-space indentation, braces on new lines for namespaces, types, and methods, and use implicit `var` only when the type is obvious. Favor `PascalCase` for classes and public members, `camelCase` for locals and parameters, and suffix async methods with `Async`. Run `dotnet format` before submitting to keep StyleCop conventions and `using` order consistent.

## Testing Guidelines
Prefer xUnit (`dotnet new xunit`) for new test projects and mirror the namespace under test. Name files `<TargetClass>Tests.cs` and keep assertions focused on observable behavior. Tests must stay hermetic—mock IMAP access, write temporary files beneath `Path.GetTempPath()`, and clean up after themselves. Extend `ArchivalSupport.Tests` when touching compression or sanitization logic; create parallel projects for other components as needed.

## Commit & Pull Request Guidelines
Use imperative, capitalized summaries (`Add config file support`). Group related changes into a single commit whenever feasible. Pull requests should describe the scenario, list validation steps (`dotnet build`, `dotnet test`, `dotnet run`), and link any tracking issues. Attach terminal output or sample archives when fixing bugs so reviewers can replay the workflow.

## Configuration & Security Notes
Never commit real Gmail credentials or archives. Copy `config.example.json` to `config.json` locally and add new keys to both files when introducing options. If a change alters credential handling or archive naming, call it out in the PR and update follow-up steps in `README.md`.
