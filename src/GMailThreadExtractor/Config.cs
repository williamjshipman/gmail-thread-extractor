using System.Text.Json;

namespace GMailThreadExtractor
{
    /// <summary>
    /// Represents the configuration options that can be loaded from a config file.
    /// These correspond to the command-line arguments supported by the application.
    /// </summary>
    public class Config
    {
        /// <summary>
        /// The email address to use for authentication.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// The password to use for authentication.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// The search query to filter emails.
        /// </summary>
        public string? Search { get; set; }

        /// <summary>
        /// The label to filter emails.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// The output file path.
        /// </summary>
        public string? Output { get; set; }

        /// <summary>
        /// The compression method to use (lzma or gzip).
        /// </summary>
        public string? Compression { get; set; }

        /// <summary>
        /// IMAP operation timeout in minutes.
        /// </summary>
        public int? TimeoutMinutes { get; set; }

        /// <summary>
        /// Loads configuration from a JSON file.
        /// </summary>
        /// <param name="configPath">The path to the JSON config file.</param>
        /// <returns>A Config object with values loaded from the file, or a new empty Config if the file doesn't exist or can't be read.</returns>
        public static async Task<Config> LoadFromFileAsync(string configPath)
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                return new Config();
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<Config>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });

                Console.WriteLine($"Config loaded from: {configPath}");
                return config ?? new Config();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config file {configPath}: {ex.Message}");
                return new Config();
            }
        }

        /// <summary>
        /// Merges this config with command-line values, giving priority to command-line arguments.
        /// </summary>
        /// <param name="cmdEmail">Command-line email value.</param>
        /// <param name="cmdPassword">Command-line password value.</param>
        /// <param name="cmdSearch">Command-line search value.</param>
        /// <param name="cmdLabel">Command-line label value.</param>
        /// <param name="cmdOutput">Command-line output value.</param>
        /// <param name="cmdCompression">Command-line compression value.</param>
        /// <param name="cmdTimeoutMinutes">Command-line timeout value.</param>
        /// <returns>A new Config object with merged values.</returns>
        public Config MergeWithCommandLine(string? cmdEmail, string? cmdPassword, string? cmdSearch, string? cmdLabel, string? cmdOutput, string? cmdCompression, int? cmdTimeoutMinutes)
        {
            return new Config
            {
                Email = !string.IsNullOrWhiteSpace(cmdEmail) ? cmdEmail : Email,
                Password = !string.IsNullOrWhiteSpace(cmdPassword) ? cmdPassword : Password,
                Search = !string.IsNullOrWhiteSpace(cmdSearch) ? cmdSearch : Search,
                Label = !string.IsNullOrWhiteSpace(cmdLabel) ? cmdLabel : Label,
                Output = !string.IsNullOrWhiteSpace(cmdOutput) ? cmdOutput : Output,
                Compression = !string.IsNullOrWhiteSpace(cmdCompression) ? cmdCompression : Compression,
                TimeoutMinutes = cmdTimeoutMinutes.HasValue ? cmdTimeoutMinutes : TimeoutMinutes
            };
        }
    }
}