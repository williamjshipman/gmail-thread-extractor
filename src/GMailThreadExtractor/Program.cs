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
                IsRequired = true
            };
            var rootCommand = new RootCommand("Extracts email threads from a Gmail account.")
            {
                emailOption,
                passwordOption,
                searchOption,
                labelOption,
                outputOption
            };

            rootCommand.SetHandler(async (email, password, search, label, output) =>
            {
                email = string.IsNullOrWhiteSpace(email) ? PromptForEmail() : email;
                password = string.IsNullOrEmpty(password) ? PromptForPassword() : password;

                var extractor = new GMailThreadExtractor(email, password, "imap.gmail.com", 993);
                await extractor.ExtractThreadsAsync(output, search, label);
            },
            emailOption, passwordOption, searchOption, labelOption, outputOption);

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
