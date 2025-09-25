using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.GZip;
using MailKit;
using Shared;

namespace ArchivalSupport;

/// <summary>
/// Provides functionality to compress email threads using the Gzip algorithm.
/// Uses SharpZipLib's Gzip implementation to compress tar-archived email threads
/// for efficient storage with wide compatibility.
/// </summary>
public class TarGzipCompressor : ICompressor
{
    /// <summary>
    /// The compression level for Gzip compression (9 = best compression).
    /// </summary>
    private const int GZIP_COMPRESSION_LEVEL = 9;

    /// <summary>
    /// Compresses the provided email threads into a tar archive and then applies Gzip compression.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of <see cref="MessageBlob"/> objects.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    public async Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads)
    {
        try
        {
            using (var fileStream = File.Create(outputPath))
            {
                using (var gzipStream = new GZipOutputStream(fileStream))
                {
                    // Set compression level for optimal compression
                    gzipStream.SetLevel(GZIP_COMPRESSION_LEVEL);

                    using (var tarStream = new TarOutputStream(gzipStream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "Gzip compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output tar.gz file if it exists.
                // The file may be partially written or corrupted.
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception ex2)
            {
                LoggingConfiguration.Logger.Warning("Unable to delete corrupted output file {OutputPath}: {ErrorMessage}", outputPath, ex2.Message);
            }

            // Re-throw the original exception to maintain proper error propagation
            throw;
        }
    }

    /// <summary>
    /// Compresses email threads using streaming download to minimize memory usage.
    /// Messages are fetched on-demand during compression rather than pre-loaded.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of IMessageSummary objects.</param>
    /// <param name="messageFetcher">Delegate to fetch message content on-demand.</param>
    /// <param name="maxMessageSizeMB">Maximum message size in MB for streaming threshold.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    public async Task CompressStreaming(string outputPath, Dictionary<ulong, List<IMessageSummary>> threads, MessageFetcher messageFetcher, int maxMessageSizeMB = 10)
    {
        try
        {
            using (var fileStream = File.Create(outputPath))
            {
                using (var gzipStream = new GZipOutputStream(fileStream))
                {
                    // Set compression level for optimal compression
                    gzipStream.SetLevel(GZIP_COMPRESSION_LEVEL);

                    using (var tarStream = new TarOutputStream(gzipStream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTarStreaming(outputPath, tarStream, threads, messageFetcher, maxMessageSizeMB);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "Gzip streaming compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output tar.gz file if it exists.
                // The file may be partially written or corrupted.
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception ex2)
            {
                LoggingConfiguration.Logger.Warning("Unable to delete corrupted output file {OutputPath}: {ErrorMessage}", outputPath, ex2.Message);
            }

            // Re-throw the original exception to maintain proper error propagation
            throw;
        }
    }
}