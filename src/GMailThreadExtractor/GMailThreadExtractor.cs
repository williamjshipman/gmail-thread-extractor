
using System.IO.Compression;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using ICSharpCode.SharpZipLib.Tar;
using SevenZip;
using System.Text;
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
                Console.WriteLine("IMAP Timeout set to: " + client.Timeout + " ms");

                await client.ConnectAsync(_imapServer, _imapPort,
                    MailKit.Security.SecureSocketOptions.SslOnConnect);
                await client.AuthenticateAsync(_email, _password);
                var allMail = client.GetFolder(SpecialFolder.All);
                await allMail.OpenAsync(FolderAccess.ReadOnly);

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
                var uids = await allMail.SearchAsync(query);

                var messages = await allMail.FetchAsync(uids,
                    MessageSummaryItems.BodyStructure |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.GMailThreadId |
                    MessageSummaryItems.Size);

                // Group messages by thread ID - this fixes both the logic bug and performance issue
                var threads = messages
                    .Where(m => m.GMailThreadId.HasValue)
                    .GroupBy(m => m.GMailThreadId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                Console.WriteLine($"Found {threads.Count} threads.");

                // Determine file extension based on compression method
                var expectedExtension = compression.ToLowerInvariant() switch
                {
                    "gzip" => ".tar.gz",
                    _ => ".tar.lzma" // Default to LZMA
                };

                if (!outputPath.EndsWith(expectedExtension))
                {
                    outputPath = outputPath + expectedExtension;
                }

                // Download the emails and save them in a new dictionary.
                var emailDictionary = new Dictionary<ulong, List<MessageBlob>>();
                var maxSizeBytes = (maxMessageSizeMB ?? 10) * 1024 * 1024; // Default 10MB limit

                foreach (var thread in threads)
                {
                    emailDictionary[thread.Key] = new List<MessageBlob>();
                    foreach (var message in thread.Value)
                    {
                        var mimeEmail = await allMail.GetMessageAsync(message.UniqueId);
                        var email = MessageWriter.MessageToBlob(message, mimeEmail, maxSizeBytes);
                        emailDictionary[thread.Key].Add(email);
                    }
                }

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
                await compressor.Compress(outputPath, emailDictionary);
                Console.WriteLine($"All done! Emails saved to {outputPath}");
            }
        }
    }
}