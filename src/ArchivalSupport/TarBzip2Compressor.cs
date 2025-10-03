using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.BZip2;
using MailKit;
using Shared;

namespace ArchivalSupport;

/// <summary>
/// Provides functionality to compress email threads using the BZip2 algorithm.
/// Uses SharpZipLib's BZip2 implementation to compress tar-archived email threads
/// for excellent compression ratios with good compatibility.
/// </summary>
public class TarBzip2Compressor : ICompressor
{
    /// <summary>
    /// Compresses the provided email threads into a tar archive and then applies BZip2 compression.
    /// Uses maximum compression level for optimal file size reduction.
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
                using (var bzip2Stream = new BZip2OutputStream(fileStream))
                {
                    // BZip2OutputStream uses maximum compression by default (block size 9)
                    // This provides the best compression ratio

                    using (var tarStream = new TarOutputStream(bzip2Stream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "BZip2 compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output tar.bz2 file if it exists.
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
                using (var bzip2Stream = new BZip2OutputStream(fileStream))
                {
                    // BZip2OutputStream uses maximum compression by default (block size 9)
                    // This provides the best compression ratio

                    using (var tarStream = new TarOutputStream(bzip2Stream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTarStreaming(outputPath, tarStream, threads, messageFetcher, maxMessageSizeMB);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "BZip2 streaming compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output tar.bz2 file if it exists.
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