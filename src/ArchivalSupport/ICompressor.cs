namespace ArchivalSupport;

public interface ICompressor
{
    /// <summary>
    /// Compresses the provided email threads into a tar archive and applies the specific compression algorithm.
    /// </summary>
    /// <param name="outputPath">The output path for the compressed archive file.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of MessageBlob objects.</param>
    /// <returns>A task representing the asynchronous compression operation.</returns>
    Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads);
}