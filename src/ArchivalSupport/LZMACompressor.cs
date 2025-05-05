using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using SevenZip;

namespace ArchivalSupport;

public class LZMACompressor : BaseCompressor
{
    private const int LZMA_ALGORITHM = 2; // LZMA algorithm
    private const int LZMA_DICTIONARY_SIZE = 256 * 1024 * 1024; // 256 MB dictionary size
    private const int LZMA_FAST_BYTES = 128; // Set fast bytes to 128 - improves compression
    private const string LZMA_MATCH_FINDER = "BT4"; // Use the "bt4" match finder

    private SevenZip.Compression.LZMA.Encoder encoder;

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

    public async void Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads)
    {
        using (var fileStream = File.Create(outputPath))
        {
            using (var memStream = new MemoryStream())
            {
                using (var tarStream = new TarOutputStream(memStream, Encoding.UTF8))
                {
                    await WriteThreadsToTar(outputPath, tarStream, threads);
                    // Need to go back to the beginning of the memory stream.
                    // encoder.Code will read from the current position in the
                    // stream to the end, so we need to reset it to the
                    // beginning.
                    memStream.Seek(0, SeekOrigin.Begin);

                    // Write the LZMA properties to the file stream.
                    encoder.WriteCoderProperties(fileStream);
                    // Now write the size of the uncompressed data.
                    Int64 fileSize = memStream.Length;
                    for (int i = 0; i < 8; i++)
                        fileStream.WriteByte((Byte)(fileSize >> (8 * i)));
                    // Finally the header info has been written, now we can
                    // compress the data.
                    encoder.Code(memStream, fileStream, -1, -1, null);
                    // Flush for good measure.
                    await fileStream.FlushAsync();
                }
            }
        }
    }
}
