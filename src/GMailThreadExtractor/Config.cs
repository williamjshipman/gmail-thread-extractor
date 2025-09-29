using System.Text.Json;
using Shared;
using Serilog;

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
        /// Maximum message size in MB to load into memory. Messages larger than this will be processed using streaming.
        /// Default is 10 MB.
        /// </summary>
        public int? MaxMessageSizeMB { get; set; }

        /// <summary>
        /// Loads configuration from a JSON file.
        /// </summary>
        /// <param name="configPath">The path to the JSON config file.</param>
        /// <returns>A Config object with values loaded from the file, or a new empty Config if the file doesn't exist or can't be read.</returns>
        public static async Task<Config> LoadFromFileAsync(string configPath)
        {
            if (!File.Exists(configPath))
            {
                LoggingConfiguration.Logger.Warning("Config file not found: {ConfigPath}", configPath);
                return new Config();
            }

            var jsonContent = await File.ReadAllTextAsync(configPath);
            Config? config;
            try
            {
                config = JsonSerializer.Deserialize<Config>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
            }
            catch (JsonException ex)
            {
                LoggingConfiguration.Logger.Error(
                    "Failed to parse configuration file '{ConfigPath}': {ErrorMessage}",
                    configPath,
                    ex.Message);
                throw new ArgumentException($"Invalid configuration file '{configPath}': {ex.Message}", ex);
            }

            LoggingConfiguration.Logger.Information("Config loaded from: {ConfigPath}", configPath);
            var finalConfig = config ?? new Config();

            try
            {
                finalConfig.Validate();
            }
            catch (ArgumentException ex)
            {
                LoggingConfiguration.Logger.Error(
                    "Invalid configuration in file '{ConfigPath}': {ErrorMessage}",
                    configPath,
                    ex.Message);
                throw new ArgumentException($"Invalid configuration in file '{configPath}': {ex.Message}", ex);
            }

            return finalConfig;
        }

        /// <summary>
        /// Validates the configuration values and throws an exception if any are invalid.
        /// </summary>
        public void Validate()
        {
            ValidateEmail();
            ValidatePassword();
            ValidateSearch();
            ValidateOutput();
            ValidateCompression();
            ValidateTimeout();
            ValidateMaxMessageSize();
        }

        private void ValidateEmail()
        {
            if (Email == null)
                return; // Will be prompted for later

            if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@') || Email.Length < 3)
                throw new ArgumentException("Email must be a valid email address format.");
        }

        private void ValidatePassword()
        {
            if (string.IsNullOrWhiteSpace(Password))
                return; // Will be prompted for later

            if (Password.Length < 1)
                throw new ArgumentException("Password cannot be empty.");
        }

        private void ValidateSearch()
        {
            if (string.IsNullOrWhiteSpace(Search))
                return; // Will be validated later as required

            if (Search.Length > 1000)
                throw new ArgumentException("Search query is too long (max 1000 characters).");
        }

        private void ValidateOutput()
        {
            if (string.IsNullOrWhiteSpace(Output))
                return; // Will be validated later as required

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(Output));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    throw new ArgumentException($"Output directory does not exist: {directory}");
                }
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"Invalid output path: {Output}");
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var fileName = Path.GetFileName(Output);
            if (!string.IsNullOrEmpty(fileName) && fileName.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException("Output filename contains invalid characters.");
            }
        }

        private void ValidateCompression()
        {
            if (string.IsNullOrWhiteSpace(Compression))
                return; // Will use default

            var validCompressions = new[] { "lzma", "gzip", "xz" };
            if (!validCompressions.Contains(Compression.ToLowerInvariant()))
            {
                throw new ArgumentException($"Compression method must be one of: {string.Join(", ", validCompressions)}");
            }
        }

        private void ValidateTimeout()
        {
            if (!TimeoutMinutes.HasValue)
                return; // Will use default

            if (TimeoutMinutes.Value < 1 || TimeoutMinutes.Value > 60)
            {
                throw new ArgumentException("Timeout must be between 1 and 60 minutes.");
            }
        }

        private void ValidateMaxMessageSize()
        {
            if (!MaxMessageSizeMB.HasValue)
                return; // Will use default

            if (MaxMessageSizeMB.Value < 1 || MaxMessageSizeMB.Value > 1000)
            {
                throw new ArgumentException("Max message size must be between 1 and 1000 MB.");
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
        /// <param name="cmdMaxMessageSizeMB">Command-line max message size value.</param>
        /// <returns>A new Config object with merged values.</returns>
        public Config MergeWithCommandLine(string? cmdEmail, string? cmdPassword, string? cmdSearch, string? cmdLabel, string? cmdOutput, string? cmdCompression, int? cmdTimeoutMinutes, int? cmdMaxMessageSizeMB = null)
        {
            var merged = new Config
            {
                Email = !string.IsNullOrWhiteSpace(cmdEmail) ? cmdEmail : Email,
                Password = !string.IsNullOrWhiteSpace(cmdPassword) ? cmdPassword : Password,
                Search = !string.IsNullOrWhiteSpace(cmdSearch) ? cmdSearch : Search,
                Label = !string.IsNullOrWhiteSpace(cmdLabel) ? cmdLabel : Label,
                Output = !string.IsNullOrWhiteSpace(cmdOutput) ? cmdOutput : Output,
                Compression = !string.IsNullOrWhiteSpace(cmdCompression) ? cmdCompression : Compression,
                TimeoutMinutes = cmdTimeoutMinutes.HasValue ? cmdTimeoutMinutes : TimeoutMinutes,
                MaxMessageSizeMB = cmdMaxMessageSizeMB.HasValue ? cmdMaxMessageSizeMB : MaxMessageSizeMB
            };

            // Validate the merged configuration
            merged.Validate();
            return merged;
        }
    }
}
