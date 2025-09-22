using System.Text;
using ICSharpCode.SharpZipLib.Tar;

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
            string folderName = $"{thread.Key} {thread.Value[0].Subject}/";
            var folderEntry = TarEntry.CreateTarEntry(folderName);
            tarStream.PutNextEntry(folderEntry);
            Console.WriteLine($"Thread ID: {thread.Key}");
            foreach (var message in thread.Value)
            {
                try
                {
                    Console.WriteLine(message.ToString());

                    // Save the message to the tar file
                    var outputEmlPath = $"{folderEntry.Name}{message.FileName}";
                    var tarEntry = TarEntry.CreateTarEntry(outputEmlPath);
                    tarEntry.Size = message.Size;
                    tarEntry.ModTime = message.Date.ToUniversalTime();
                    tarStream.PutNextEntry(tarEntry);
                    try
                    {
                        await tarStream.WriteAsync(message.Blob);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing message to tar: {ex.Message}");
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
                    Console.WriteLine($"Error processing message: {ex.Message}");
                }
            }
            tarStream.CloseEntry();
            Console.WriteLine($"Saved thread to: {outputPath}/{folderEntry.Name}");
        }

        await tarStream.FlushAsync();
    }
}