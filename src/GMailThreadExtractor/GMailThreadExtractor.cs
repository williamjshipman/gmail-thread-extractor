
using System.IO.Compression;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using ICSharpCode.SharpZipLib.Tar;
using SevenZip;
using System.Text;

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

        public GMailThreadExtractor(string email, string password, string imapServer, int imapPort)
        {
            _email = email;
            _password = password;
            _imapServer = imapServer;
            _imapPort = imapPort;
        }

        public async Task ExtractThreadsAsync(string outputPath, string searchQuery, string label)
        {
            // Connect to the IMAP server and authenticate
            using (var client = new ImapClient())
            {
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
                        //var threadList = threads.TryGetValue(threadId, out var threadMessages) ? threadMessages : null;
                        var threadUuids = await allMail.SearchAsync(SearchQuery.All.And(SearchQuery.GMailThreadId(threadId)));
                        var threadList = await allMail.FetchAsync(threadUuids,
                            MessageSummaryItems.BodyStructure |
                            MessageSummaryItems.Envelope |
                            MessageSummaryItems.UniqueId |
                            MessageSummaryItems.GMailThreadId |
                            MessageSummaryItems.Size);
                        threads[threadId] = threadList.ToList();
                    }
                }

                Console.WriteLine($"Found {threads.Count} threads.");
                if (!outputPath.EndsWith(".tar.lzma"))
                {
                    outputPath = outputPath + ".tar.lzma";
                }
                SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();
                encoder.SetCoderProperties(
                    [
                        CoderPropID.Algorithm,
                        CoderPropID.DictionarySize,
                        CoderPropID.NumFastBytes,
                        CoderPropID.MatchFinder,
                        // CoderPropID.BlockSize
                    ],
                    [
                        2, // LZMA algorithm, I hope.
                        256*1024*1024, // 256 MB dictionary size.
                        128, // Set fast bytes to 128 - improves compression.
                        "BT4", // Use the "bt4" match finder.
                        // 4*1024*1024 // 16 GB block size.
                    ]);
                using (var fileStream = File.Create(outputPath))
                {
                    using (var memStream = new MemoryStream())
                    {
                        using (var tarStream = new TarOutputStream(memStream, Encoding.UTF8))
                        {
                            foreach (var thread in threads)
                            {
                                string folderName = $"{thread.Key} {thread.Value[0].Envelope.Subject}/";
                                var folderEntry = TarEntry.CreateTarEntry(folderName);
                                tarStream.PutNextEntry(folderEntry);
                                Console.WriteLine($"Thread ID: {thread.Key}");
                                foreach (var message in thread.Value)
                                {
                                    try
                                    {
                                        Console.WriteLine($"Subject: {message.Envelope.Subject}");
                                        Console.WriteLine($"From: {message.Envelope.From}");
                                        Console.WriteLine($"To: {message.Envelope.To}");
                                        Console.WriteLine($"Date: {message.Date}");
                                        Console.WriteLine($"Size: {message.Size} bytes");
                                        Console.WriteLine($"UID: {message.UniqueId}");

                                        IMimeMessage? mimeMessage = await allMail.GetMessageAsync(message.UniqueId);
                                        if (mimeMessage == null)
                                        {
                                            Console.WriteLine("Failed to retrieve message.");
                                            continue;
                                        }
                                        // Save the message to the tar file
                                        // var outputEmlPath = message.UniqueId.ToString() + ".eml";
                                        var outputEmlPath = $"{message.Envelope.From}_{message.Date.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss")}.eml";
                                        var tarEntry = TarEntry.CreateTarEntry(outputEmlPath);
                                        tarEntry.Size = message.Size.HasValue ? message.Size.Value : mimeMessage.ToString().Length;
                                        tarEntry.ModTime = message.Date.UtcDateTime;
                                        tarStream.PutNextEntry(tarEntry);
                                        try
                                        {
                                            mimeMessage.WriteTo(tarStream);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error writing message to tar: {ex.Message}");
                                        }
                                        finally
                                        {
                                            tarStream.CloseEntry();
                                        }
                                        Console.WriteLine($"Saved to: {outputPath}/{folderEntry.Name}{outputEmlPath}");    

                                        Console.WriteLine();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error processing message: {ex.Message}");
                                    }
                                }
                                tarStream.CloseEntry();
                                Console.WriteLine($"Saved thread to: {outputPath}/{folderEntry.Name}");
                            }
                            await tarStream.FlushAsync();
                            memStream.Seek(0, SeekOrigin.Begin);
                            encoder.WriteCoderProperties(fileStream);
                            Int64 fileSize = memStream.Length;
                            for (int i = 0; i < 8; i++)
                                fileStream.WriteByte((Byte)(fileSize >> (8 * i)));
                            encoder.Code(memStream, fileStream, -1, -1, null);
                            await fileStream.FlushAsync();
                        }
                    }
                }
            }
        }
    }
}