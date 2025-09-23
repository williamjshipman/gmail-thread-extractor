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
    /// The dictionary size for LZMA compression (256 MB).
    /// </summary>
    private const int LZMA_DICTIONARY_SIZE = 256 * 1024 * 1024; // 256 MB dictionary size

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
    /// Compresses the provided email threads into a tar archive and then applies LZMA compression.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of <see cref="MessageBlob"/> objects.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    public async Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads)
    {
        string sTempTarFilePath = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using (var fileStream = File.Create(outputPath))
            {
                using (var tempTarFileStream = new FileStream(sTempTarFilePath, FileMode.Create, FileAccess.Write))
                {
                    using (var tarStream = new TarOutputStream(tempTarFileStream, Encoding.UTF8))
                    {
                        await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
                    }
                }
                // Closing the temporary tar stream to ensure all data is written.
                using (var tempTarFileStream = new FileStream(sTempTarFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Write the LZMA properties to the file stream.
                    encoder.WriteCoderProperties(fileStream);
                    // Now write the size of the uncompressed data.
                    Int64 fileSize = tempTarFileStream.Length;
                    for (int i = 0; i < 8; i++)
                        fileStream.WriteByte((Byte)(fileSize >> (8 * i)));
                    // Finally the header info has been written, now we can
                    // compress the data.
                    encoder.Code(tempTarFileStream, fileStream, -1, -1, null);
                    // Flush for good measure.
                    await fileStream.FlushAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during compression: {ex.Message}");
            try
            {
                // Delete the output tar.lzma file if it exists.
                // The file may be partially written or corrupted.
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"Warning: Unable to delete temporary file {outputPath}. {ex2.Message}");
            }
        }
        finally
        {
            try
            {
                // Delete the temporary tar file.
                if (File.Exists(sTempTarFilePath))
                    File.Delete(sTempTarFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Unable to delete temporary file {sTempTarFilePath}. {ex.Message}");
            }
        }
    }
}
