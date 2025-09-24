using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using SevenZip;
using MailKit;
using Shared;

namespace ArchivalSupport;

/// <summary>
/// Provides functionality to compress email threads using the LZMA algorithm.
/// Inherits from <see cref="BaseCompressor"/> and uses SevenZip LZMA encoder to
/// compress tar-archived email threads for efficient storage.
/// </summary>
public class LZMACompressor : ICompressor
{
    /// <summary>
    /// The LZMA algorithm identifier.
    /// </summary>
    private const int LZMA_ALGORITHM = 2; // LZMA algorithm

    /// <summary>
    /// The dictionary size for LZMA compression (64 MB).
    /// </summary>
    private const int LZMA_DICTIONARY_SIZE = 64 * 1024 * 1024;

    /// <summary>
    /// The number of fast bytes used by the LZMA encoder (128).
    /// </summary>
    private const int LZMA_FAST_BYTES = 128; // Set fast bytes to 128 - improves compression

    /// <summary>
    /// The match finder algorithm used by LZMA ("BT4").
    /// </summary>
    private const string LZMA_MATCH_FINDER = "BT4"; // Use the "bt4" match finder

    /// <summary>
    /// The LZMA encoder instance.
    /// </summary>
    private SevenZip.Compression.LZMA.Encoder encoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="LZMACompressor"/> class and sets up encoder properties.
    /// </summary>
    public LZMACompressor()
    {
        encoder = new SevenZip.Compression.LZMA.Encoder();
        encoder.SetCoderProperties(
            [
                CoderPropID.Algorithm,
                CoderPropID.DictionarySize,
                CoderPropID.NumFastBytes,
                CoderPropID.MatchFinder
            ],
            [
                LZMA_ALGORITHM,
                LZMA_DICTIONARY_SIZE,
                LZMA_FAST_BYTES,
                LZMA_MATCH_FINDER
            ]);
    }

    /// <summary>
    /// Compresses the provided email threads using a memory-efficient streaming approach.
    /// Uses a temporary file for intermediate tar data but with minimal memory usage during I/O.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of <see cref="MessageBlob"/> objects.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    public async Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads)
    {
        string tempTarFilePath = Path.Combine(Path.GetTempPath(), $"lzma_temp_{Guid.NewGuid():N}.tar");

        try
        {
            // Step 1: Create tar file with streaming I/O (low memory usage)
            using (var tempFileStream = new FileStream(tempTarFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192))
            {
                using (var tarStream = new TarOutputStream(tempFileStream, Encoding.UTF8))
                {
                    await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
                }
            }

            // Step 2: Get file size for LZMA header
            var tempFileInfo = new FileInfo(tempTarFilePath);
            long uncompressedSize = tempFileInfo.Length;

            // Step 3: Compress with streaming I/O (low memory usage)
            using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192))
            {
                // Write LZMA properties
                encoder.WriteCoderProperties(outputStream);

                // Write uncompressed size (8 bytes)
                for (int i = 0; i < 8; i++)
                    outputStream.WriteByte((byte)(uncompressedSize >> (8 * i)));

                // Compress the tar file with streaming
                using (var inputStream = new FileStream(tempTarFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192))
                {
                    encoder.Code(inputStream, outputStream, uncompressedSize, -1, null);
                }

                await outputStream.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "LZMA compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output file if it exists and is potentially corrupted
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"Warning: Unable to delete output file {outputPath}. {ex2.Message}");
            }
            throw; // Re-throw to maintain error handling contract
        }
        finally
        {
            // Always clean up temporary file
            try
            {
                if (File.Exists(tempTarFilePath))
                    File.Delete(tempTarFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Unable to delete temporary file {tempTarFilePath}. {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Compresses email threads using streaming download to minimize memory usage.
    /// Uses a two-pass approach: first pass calculates size, second pass performs compression.
    /// Messages are fetched on-demand during both passes to minimize memory usage.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of IMessageSummary objects.</param>
    /// <param name="messageFetcher">Delegate to fetch message content on-demand.</param>
    /// <param name="maxMessageSizeMB">Maximum message size in MB for streaming threshold.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    public async Task CompressStreaming(string outputPath, Dictionary<ulong, List<IMessageSummary>> threads, MessageFetcher messageFetcher, int maxMessageSizeMB = 10)
    {
        string tempTarFilePath = Path.Combine(Path.GetTempPath(), $"lzma_streaming_temp_{Guid.NewGuid():N}.tar");

        try
        {
            Console.WriteLine("LZMA streaming compression: Creating tar archive...");

            // Pass 1: Create tar file using streaming (one message at a time)
            using (var tempFileStream = new FileStream(tempTarFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192))
            {
                using (var tarStream = new TarOutputStream(tempFileStream, Encoding.UTF8))
                {
                    await BaseCompressor.WriteThreadsToTarStreaming(outputPath, tarStream, threads, messageFetcher, maxMessageSizeMB);
                }
            }

            Console.WriteLine("LZMA streaming compression: Compressing tar archive...");

            // Pass 2: Compress the tar file with streaming I/O
            var tempFileInfo = new FileInfo(tempTarFilePath);
            long uncompressedSize = tempFileInfo.Length;

            using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192))
            {
                // Write LZMA properties
                encoder.WriteCoderProperties(outputStream);

                // Write uncompressed size (8 bytes)
                for (int i = 0; i < 8; i++)
                    outputStream.WriteByte((byte)(uncompressedSize >> (8 * i)));

                // Compress the tar file with streaming
                using (var inputStream = new FileStream(tempTarFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192))
                {
                    encoder.Code(inputStream, outputStream, uncompressedSize, -1, null);
                }

                await outputStream.FlushAsync();
            }

            Console.WriteLine("LZMA streaming compression: Complete!");
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "LZMA streaming compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                // Delete the output file if it exists and is potentially corrupted
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"Warning: Unable to delete output file {outputPath}. {ex2.Message}");
            }
            throw; // Re-throw to maintain error handling contract
        }
        finally
        {
            // Always clean up temporary file
            try
            {
                if (File.Exists(tempTarFilePath))
                    File.Delete(tempTarFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Unable to delete temporary file {tempTarFilePath}. {ex.Message}");
            }
        }
    }
}
