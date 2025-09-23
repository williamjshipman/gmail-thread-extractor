using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.GZip;

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
}