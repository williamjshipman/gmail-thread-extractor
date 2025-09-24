using System;
using System.CommandLine;
using System.Text;

namespace GMailThreadExtractor
{
    /// <summary>
    /// Hosts the application entry point and orchestrates command-line handling for the Gmail thread extractor.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point that builds the command-line interface and invokes the extraction workflow.
        /// </summary>
        /// <param name="args">Command-line arguments provided by the user.</param>
        /// <returns>The process exit code produced by System.CommandLine.</returns>
        static async Task<int> Main(string[] args)
        {
            var configOption = new Option<string>(
                name: "--config",
                description: "Path to the JSON configuration file.")
            {
                IsRequired = false
            };
            var emailOption = new Option<string>(
                name: "--email",
                description: "The email address to use for authentication.")
            {
                IsRequired = false
            };
            var passwordOption = new Option<string>(
                name: "--password",
                description: "The password to use for authentication.")
            {
                IsRequired = false
            };
            var searchOption = new Option<string>(
                name: "--search",
                description: "The search query to filter emails.")
            {
                IsRequired = false
            };
            var labelOption = new Option<string>(
                name: "--label",
                description: "The label to filter emails.")
            {
                IsRequired = false
            };
            var outputOption = new Option<string>(
                name: "--output",
                description: "The output file path.")
            {
                IsRequired = false
            };
            var compressionOption = new Option<string>(
                name: "--compression",
                description: "The compression method to use (lzma or gzip). Default is lzma.")
            {
                IsRequired = false
            };
            compressionOption.AddValidator(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (!string.IsNullOrEmpty(value) && value != "lzma" && value != "gzip")
                {
                    result.ErrorMessage = "Compression method must be either 'lzma' or 'gzip'.";
                }
            });
            var timeoutOption = new Option<int?>(
                name: "--timeout",
                description: "IMAP operation timeout in minutes. Default is 5 minutes.")
            {
                IsRequired = false
            };
            timeoutOption.AddValidator(result =>
            {
                var value = result.GetValueOrDefault<int?>();
                if (value.HasValue && (value.Value < 1 || value.Value > 60))
                {
                    result.ErrorMessage = "Timeout must be between 1 and 60 minutes.";
                }
            });
            var rootCommand = new RootCommand("Extracts email threads from a Gmail account.")
            {
                configOption,
                emailOption,
                passwordOption,
                searchOption,
                labelOption,
                outputOption,
                compressionOption,
                timeoutOption
            };

            rootCommand.SetHandler(async (configPath, email, password, search, label, output, compression, timeoutMinutes) =>
            {
                // Load config file if specified
                var config = new Config();
                if (!string.IsNullOrWhiteSpace(configPath))
                {
                    config = await Config.LoadFromFileAsync(configPath);
                }
                else
                {
                    // Try to load from default config file locations
                    var defaultConfigPaths = new[]
                    {
                        "config.json",
                        "gmail-extractor.json",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gmail-extractor.json")
                    };

                    foreach (var defaultPath in defaultConfigPaths)
                    {
                        if (File.Exists(defaultPath))
                        {
                            config = await Config.LoadFromFileAsync(defaultPath);
                            break;
                        }
                    }
                }

                if (timeoutMinutes <= 0)
                {
                    // Use default if invalid
                    // System.CommandLine should give a null value if not specified but instead gives 0.
                    // We handle that here by converting 0 or negative values to null.
                    timeoutMinutes = null;
                }

                // Merge config with command line arguments (command line takes precedence)
                var finalConfig = config.MergeWithCommandLine(email, password, search, label, output, compression, timeoutMinutes);

                // Validate required parameters and prompt if needed
                finalConfig.Email = string.IsNullOrWhiteSpace(finalConfig.Email) ? PromptForEmail() : finalConfig.Email;
                finalConfig.Password = string.IsNullOrWhiteSpace(finalConfig.Password) ? PromptForPassword() : finalConfig.Password;

                if (string.IsNullOrWhiteSpace(finalConfig.Output))
                {
                    throw new ArgumentException("Output file path is required. Specify --output or include 'output' in config file.");
                }

                if (string.IsNullOrWhiteSpace(finalConfig.Search))
                {
                    throw new ArgumentException("Search query is required. Specify --search or include 'search' in config file.");
                }

                var timeout = finalConfig.TimeoutMinutes.HasValue ? TimeSpan.FromMinutes(finalConfig.TimeoutMinutes.Value) : (TimeSpan?)null;
                var extractor = new GMailThreadExtractor(finalConfig.Email, finalConfig.Password, "imap.gmail.com", 993, timeout);
                await extractor.ExtractThreadsAsync(finalConfig.Output, finalConfig.Search, finalConfig.Label ?? string.Empty, finalConfig.Compression ?? "lzma", finalConfig.MaxMessageSizeMB);
            },
            configOption, emailOption, passwordOption, searchOption, labelOption, outputOption, compressionOption, timeoutOption);

            return await rootCommand.InvokeAsync(args);
        }

        /// <summary>
        /// Prompts the user for their email address until a non-empty value is provided.
        /// </summary>
        /// <returns>The email address supplied by the user.</returns>
        private static string PromptForEmail()
        {
            while (true)
            {
                Console.Write("Email: ");
                var input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    return input;
                }
            }
        }

        /// <summary>
        /// Prompts the user for their password until a non-empty value is provided.
        /// </summary>
        /// <returns>The password supplied by the user.</returns>
        private static string PromptForPassword()
        {
            while (true)
            {
                Console.Write("Password: ");
                var password = ReadHiddenInput();
                Console.WriteLine(); // Move to the next line after password entry.
                if (!string.IsNullOrEmpty(password))
                {
                    return password;
                }
            }
        }

        /// <summary>
        /// Reads user input without echoing characters to the console.
        /// </summary>
        /// <returns>The string entered by the user.</returns>
        private static string ReadHiddenInput()
        {
            var builder = new StringBuilder();

            while (true)
            {
                var keyInfo = Console.ReadKey(intercept: true); // Intercept prevents the character from being displayed.

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    return builder.ToString();
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0)
                    {
                        builder.Remove(builder.Length - 1, 1);
                    }

                    continue;
                }

                if (!char.IsControl(keyInfo.KeyChar))
                {
                    builder.Append(keyInfo.KeyChar);
                }
            }
        }
    }
}

