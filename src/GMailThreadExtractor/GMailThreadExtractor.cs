using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using ArchivalSupport;

namespace GMailThreadExtractor
{
    /// <summary>
    /// This class is responsible for extracting email threads from a Gmail account using IMAP.
    /// It connects to the Gmail server, authenticates the user, and retrieves emails based on
    /// the specified search criteria and label.
    /// </summary>
    internal class GMailThreadExtractor
    {
        private readonly string _email;
        private readonly string _password;
        private readonly string _imapServer;
        private readonly int _imapPort;
        private readonly TimeSpan _timeout;

        public GMailThreadExtractor(string email, string password, string imapServer, int imapPort, TimeSpan? timeout = null)
        {
            _email = email;
            _password = password;
            _imapServer = imapServer;
            _imapPort = imapPort;
            _timeout = timeout ?? TimeSpan.FromMinutes(5); // Default 5 minute timeout
        }

        public async Task ExtractThreadsAsync(string outputPath, string searchQuery, string label, string compression = "lzma", int? maxMessageSizeMB = null)
        {
            // Connect to the IMAP server and authenticate
            using (var client = new ImapClient())
            {
                // Configure timeout for all IMAP operations
                client.Timeout = (int)_timeout.TotalMilliseconds;
                LoggingConfiguration.Logger.Information("IMAP Timeout set to: {TimeoutMs} ms", client.Timeout);

                // Connect with retry logic
                await RetryHelper.ExecuteWithRetryAsync(
                    async () => await client.ConnectAsync(_imapServer, _imapPort, MailKit.Security.SecureSocketOptions.SslOnConnect),
                    maxAttempts: 3,
                    baseDelay: TimeSpan.FromSeconds(2),
                    operationName: "IMAP connection");

                // Authenticate with retry logic
                await RetryHelper.ExecuteWithRetryAsync(
                    async () => await client.AuthenticateAsync(_email, _password),
                    maxAttempts: 2, // Fewer attempts for auth to avoid account lockout
                    baseDelay: TimeSpan.FromSeconds(1),
                    operationName: "IMAP authentication");

                var allMail = client.GetFolder(SpecialFolder.All);

                // Open folder with retry logic
                await RetryHelper.ExecuteWithRetryAsync(
                    async () => await allMail.OpenAsync(FolderAccess.ReadOnly),
                    maxAttempts: 3,
                    baseDelay: TimeSpan.FromSeconds(1),
                    operationName: "opening All Mail folder");

                // Search for emails based on the provided criteria
                var queries = new List<SearchQuery>();
                if (!string.IsNullOrEmpty(searchQuery))
                {
                    queries.Add(SearchQuery.GMailRawSearch(searchQuery));
                }
                if (!string.IsNullOrEmpty(label))
                {
                    queries.Add(SearchQuery.HasGMailLabel(label));
                }
                var query = SearchQuery.All;
                if (queries.Count > 0)
                {
                    query = queries.Aggregate(query, (current, q) => current.And(q));
                }
                // Search for emails with retry logic
                var uids = await RetryHelper.ExecuteWithRetryAsync(
                    async () => await allMail.SearchAsync(query),
                    maxAttempts: 3,
                    baseDelay: TimeSpan.FromSeconds(1),
                    operationName: "searching for emails");

                // Fetch message summaries with retry logic
                var messages = await RetryHelper.ExecuteWithRetryAsync(
                    async () => await allMail.FetchAsync(uids,
                        MessageSummaryItems.BodyStructure |
                        MessageSummaryItems.Envelope |
                        MessageSummaryItems.UniqueId |
                        MessageSummaryItems.GMailThreadId |
                        MessageSummaryItems.Size),
                    maxAttempts: 3,
                    baseDelay: TimeSpan.FromSeconds(2),
                    operationName: "fetching message summaries");

                var threads = new Dictionary<ulong, List<IMessageSummary>>();
                foreach (var message in messages)
                {
                    if (message.GMailThreadId.HasValue)
                    {
                        ulong threadId = message.GMailThreadId.Value;
                        if (threads.ContainsKey(threadId))
                        {
                            continue; // Skip if we already have this thread.
                        }
                        // Search for thread messages with retry logic
                        var threadUuids = await RetryHelper.ExecuteWithRetryAsync(
                            async () => await allMail.SearchAsync(SearchQuery.All.And(SearchQuery.GMailThreadId(threadId))),
                            maxAttempts: 3,
                            baseDelay: TimeSpan.FromSeconds(1),
                            operationName: $"searching thread {threadId}");

                        // Fetch thread messages with retry logic
                        var threadList = await RetryHelper.ExecuteWithRetryAsync(
                            async () => await allMail.FetchAsync(threadUuids,
                                MessageSummaryItems.BodyStructure |
                                MessageSummaryItems.Envelope |
                                MessageSummaryItems.UniqueId |
                                MessageSummaryItems.GMailThreadId |
                                MessageSummaryItems.Size),
                            maxAttempts: 3,
                            baseDelay: TimeSpan.FromSeconds(1),
                            operationName: $"fetching thread {threadId} messages");
                        threads[threadId] = threadList.ToList();
                    }
                }

                LoggingConfiguration.Logger.Information("Found {ThreadCount} threads.", threads.Count);

                // Determine file extension based on compression method
                var expectedExtension = compression.ToLowerInvariant() switch
                {
                    "gzip" => ".tar.gz",
                    _ => ".tar.lzma" // Default to LZMA
                };

                if (!outputPath.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = outputPath + expectedExtension;
                }

                // Use streaming compression to minimize memory usage
                var maxSizeMB = maxMessageSizeMB ?? 10; // Default 10MB limit

                // Create message fetcher delegate for streaming
                MessageFetcher messageFetcher = async (messageSummary) =>
                {
                    // Download email message with retry logic
                    var mimeEmail = await RetryHelper.ExecuteWithRetryAsync(
                        async () => await allMail.GetMessageAsync(messageSummary.UniqueId),
                        maxAttempts: 3,
                        baseDelay: TimeSpan.FromSeconds(1),
                        operationName: $"downloading message {messageSummary.UniqueId}");

                    var maxSizeBytes = maxSizeMB * 1024L * 1024L;
                    return MessageWriter.MessageToBlob(messageSummary, mimeEmail, maxSizeBytes);
                };

                // Select compressor based on compression method
                ICompressor compressor;
                switch (compression.ToLowerInvariant())
                {
                    case "gzip":
                        compressor = new TarGzipCompressor();
                        break;
                    case "lzma":
                    default:
                        compressor = new LZMACompressor();
                        break;
                }

                // Use streaming compression instead of pre-loading all messages
                LoggingConfiguration.Logger.Information("Starting streaming compression to minimize memory usage...");
                await compressor.CompressStreaming(outputPath, threads, messageFetcher, maxSizeMB);
                LoggingConfiguration.Logger.Information("All done! Emails saved to {OutputPath}", outputPath);
            }
        }
    }
}
