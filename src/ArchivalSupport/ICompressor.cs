using MailKit;

namespace ArchivalSupport;

/// <summary>
/// Delegate for fetching a message on-demand during compression to enable streaming.
/// </summary>
/// <param name="messageSummary">The message summary containing metadata.</param>
/// <returns>A MessageBlob containing the message data.</returns>
public delegate Task<MessageBlob> MessageFetcher(IMessageSummary messageSummary);

public interface ICompressor
{
    /// <summary>
    /// Compresses the provided email threads into a tar archive and applies the specific compression algorithm.
    /// </summary>
    /// <param name="outputPath">The output path for the compressed archive file.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of MessageBlob objects.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads);

    /// <summary>
    /// Compresses email threads using streaming download to minimize memory usage.
    /// Messages are fetched on-demand during compression rather than pre-loaded.
    /// </summary>
    /// <param name="outputPath">The output path for the compressed archive file.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of IMessageSummary objects.</param>
    /// <param name="messageFetcher">Delegate to fetch message content on-demand.</param>
    /// <param name="maxMessageSizeMB">Maximum message size in MB for streaming threshold.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    Task CompressStreaming(string outputPath, Dictionary<ulong, List<IMessageSummary>> threads, MessageFetcher messageFetcher, int maxMessageSizeMB = 10);
}