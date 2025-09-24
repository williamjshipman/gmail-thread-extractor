using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using SevenZip;

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
    private const int LZMA_DICTIONARY_SIZE = 64 * 1024 * 1024; // 16 MB dictionary size

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
    /// Compresses the provided email threads directly into an LZMA stream without creating temporary files.
    /// Uses streaming compression to minimize memory usage.
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
                // Write LZMA properties header
                encoder.WriteCoderProperties(fileStream);

                // We need to write uncompressed size, but we don't know it yet
                // Write placeholder for size (8 bytes) - will be updated later
                long sizePosition = fileStream.Position;
                for (int i = 0; i < 8; i++)
                    fileStream.WriteByte(0);

                // Create a stream that will compress data as it's written
                using (var lzmaStream = new LZMAOutputStream(fileStream, encoder))
                {
                    using (var tarStream = new TarOutputStream(lzmaStream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
                    }

                    // Get the uncompressed size
                    long uncompressedSize = lzmaStream.UncompressedSize;

                    // Go back and write the actual uncompressed size
                    fileStream.Position = sizePosition;
                    for (int i = 0; i < 8; i++)
                        fileStream.WriteByte((byte)(uncompressedSize >> (8 * i)));
                }

                await fileStream.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during compression: {ex.Message}");
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
    }
}
