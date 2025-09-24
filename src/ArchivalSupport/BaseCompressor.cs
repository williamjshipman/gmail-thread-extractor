using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using MailKit;
using Shared;

namespace ArchivalSupport;

/// <summary>
/// Provides base functionality for compressing and archiving email threads.
/// </summary>
public static class BaseCompressor
{
    /// <summary>
    /// Writes the provided email threads to a tar archive stream, organizing messages by thread.
    /// </summary>
    /// <param name="outputPath">The output path for the archive file.</param>
    /// <param name="tarStream">The tar output stream to write to.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of MessageBlob objects.</param>
    public static async Task WriteThreadsToTar(
        string outputPath,
        TarOutputStream tarStream,
        Dictionary<ulong, List<MessageBlob>> threads)
    {
        foreach (var thread in threads)
        {
            if (thread.Value.Count == 0)
            {
                Console.WriteLine($"Thread ID: {thread.Key} contained no messages. Skipping.");
                continue;
            }

            var folderSegment = SafeNameBuilder.BuildThreadDirectoryName(thread.Key, thread.Value[0].Subject);
            var folderName = $"{folderSegment}/";
            var folderEntry = TarEntry.CreateTarEntry(folderName);
            tarStream.PutNextEntry(folderEntry);
            tarStream.CloseEntry();

            Console.WriteLine($"Thread ID: {thread.Key}");

            foreach (var message in thread.Value)
            {
                try
                {
                    Console.WriteLine(message.ToString());

                    // Save the message to the tar file
                    var outputEmlPath = $"{folderName}{message.FileName}";
                    var tarEntry = TarEntry.CreateTarEntry(outputEmlPath);
                    tarEntry.Size = message.Size;
                    tarEntry.ModTime = message.Date.ToUniversalTime();
                    tarStream.PutNextEntry(tarEntry);
                    try
                    {
                        if (message.IsStreaming)
                        {
                            // Use streaming for large messages
                            if (message.StreamFunc != null)
                            {
                                await message.StreamFunc(tarStream);
                            }
                        }
                        else
                        {
                            // Use in-memory data for small messages
                            if (message.Blob != null)
                            {
                                await tarStream.WriteAsync(message.Blob);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.Handle(ErrorCategory.Compression,
                            "Failed to write message to tar archive",
                            ex,
                            $"Message: {message.FileName}");
                    }
                    finally
                    {
                        tarStream.CloseEntry();
                    }

                    Console.WriteLine($"Saved to: {outputPath}/{outputEmlPath}");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    ErrorHandler.Handle(ErrorCategory.EmailProcessing,
                        "Error processing message for archival",
                        ex,
                        $"Message ID: {message.UniqueId}");
                }
            }

            Console.WriteLine($"Saved thread to: {outputPath}/{folderName}");
        }

        await tarStream.FlushAsync();
    }

    /// <summary>
    /// Writes email threads to a tar archive stream using streaming download to minimize memory usage.
    /// Messages are fetched on-demand during compression rather than pre-loaded.
    /// </summary>
    /// <param name="outputPath">The output path for the archive file.</param>
    /// <param name="tarStream">The tar output stream to write to.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of IMessageSummary objects.</param>
    /// <param name="messageFetcher">Delegate to fetch message content on-demand.</param>
    /// <param name="maxMessageSizeMB">Maximum message size in MB for streaming threshold.</param>
    public static async Task WriteThreadsToTarStreaming(
        string outputPath,
        TarOutputStream tarStream,
        Dictionary<ulong, List<IMessageSummary>> threads,
        MessageFetcher messageFetcher,
        int maxMessageSizeMB = 10)
    {
        var maxSizeBytes = maxMessageSizeMB * 1024L * 1024L; // Convert to bytes

        foreach (var thread in threads)
        {
            if (thread.Value.Count == 0)
            {
                Console.WriteLine($"Thread ID: {thread.Key} contained no messages. Skipping.");
                continue;
            }

            var folderSegment = SafeNameBuilder.BuildThreadDirectoryName(thread.Key, thread.Value[0].Envelope?.Subject);
            var folderName = $"{folderSegment}/";
            var folderEntry = TarEntry.CreateTarEntry(folderName);
            tarStream.PutNextEntry(folderEntry);
            tarStream.CloseEntry();

            Console.WriteLine($"Thread ID: {thread.Key}");

            foreach (var messageSummary in thread.Value)
            {
                try
                {
                    Console.WriteLine($"Processing message {messageSummary.UniqueId} (Subject: {messageSummary.Envelope?.Subject ?? "No Subject"})");

                    // Fetch message on-demand
                    var messageBlob = await messageFetcher(messageSummary);

                    // Save the message to the tar file
                    var outputEmlPath = $"{folderName}{messageBlob.FileName}";
                    var tarEntry = TarEntry.CreateTarEntry(outputEmlPath);
                    tarEntry.Size = messageBlob.Size;
                    tarEntry.ModTime = messageBlob.Date.ToUniversalTime();
                    tarStream.PutNextEntry(tarEntry);
                    try
                    {
                        if (messageBlob.IsStreaming)
                        {
                            // Use streaming for large messages
                            if (messageBlob.StreamFunc != null)
                            {
                                await messageBlob.StreamFunc(tarStream);
                            }
                        }
                        else
                        {
                            // Use in-memory data for small messages
                            if (messageBlob.Blob != null)
                            {
                                await tarStream.WriteAsync(messageBlob.Blob);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.Handle(ErrorCategory.Compression,
                            "Failed to write message to tar archive",
                            ex,
                            $"Message: {messageBlob.FileName}");
                    }
                    finally
                    {
                        tarStream.CloseEntry();
                    }

                    Console.WriteLine($"Saved to: {outputPath}/{outputEmlPath}");
                    Console.WriteLine();

                    // Force garbage collection after each message to minimize memory usage
                    if (messageBlob.Size > maxSizeBytes / 2) // GC for messages > 5MB by default
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.Handle(ErrorCategory.EmailProcessing,
                        "Error processing message for streaming archival",
                        ex,
                        $"Message ID: {messageSummary.UniqueId}");
                }
            }

            Console.WriteLine($"Saved thread to: {outputPath}/{folderName}");
        }

        await tarStream.FlushAsync();
    }
}
