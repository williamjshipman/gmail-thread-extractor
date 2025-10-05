using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using MailKit;
using Joveler.Compression.XZ;
using Shared;

namespace ArchivalSupport;

/// <summary>
/// Provides functionality to compress email threads into tar.xz archives using Joveler.Compression.XZ.
/// </summary>
public class TarXzCompressor : ICompressor
{
    static TarXzCompressor()
    {
        // Initialize Joveler.Compression.XZ library with platform-specific path resolution
        try
        {
            // Try to locate the correct native library based on platform
            var baseDir = Path.GetDirectoryName(typeof(TarXzCompressor).Assembly.Location);
            var runtimeDir = Path.Combine(baseDir!, "runtimes");

            string? nativeLibPath = null;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-x64", "native", "liblzma.dll");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X86)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-x86", "native", "liblzma.dll");
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                {
                    nativeLibPath = Path.Combine(runtimeDir, "win-arm64", "native", "liblzma.dll");
                }
            }

            if (nativeLibPath != null && File.Exists(nativeLibPath))
            {
                XZInit.GlobalInit(nativeLibPath);
            }
            else
            {
                // Fallback to default initialization
                XZInit.GlobalInit();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already initialized"))
        {
            // Library is already initialized, this is fine - no need to re-initialize
            return;
        }
        catch (Exception ex)
        {
            // Log error and attempt default initialization as fallback
            try
            {
                LoggingConfiguration.Logger?.Warning("XZ library initialization with explicit path failed: {ErrorMessage}", ex.Message);
            }
            catch
            {
                // Ignore logging errors during static constructor
            }

            try
            {
                XZInit.GlobalInit();
            }
            catch (InvalidOperationException fallbackEx) when (fallbackEx.Message.Contains("already initialized"))
            {
                // Library is already initialized, this is fine
                return;
            }
            catch (Exception fallbackEx)
            {
                try
                {
                    LoggingConfiguration.Logger?.Error("XZ library initialization failed completely: {ErrorMessage}", fallbackEx.Message);
                }
                catch
                {
                    // Ignore logging errors during static constructor
                }
                throw;
            }
        }
    }

    /// <summary>
    /// Compresses the provided email threads into a tar archive and then applies true XZ compression.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of <see cref="MessageBlob"/> objects.</param>
    public async Task Compress(string outputPath, Dictionary<ulong, List<MessageBlob>> threads)
    {
        var (tempFileStream, tempTarFilePath) = SecureIOUtilities.CreateSecureTempFile("xz_temp", ".tar");

        try
        {
            // Step 1: Create TAR archive using SharpZipLib
            using (tempFileStream)
            {
                using var tarStream = new TarOutputStream(tempFileStream, Encoding.UTF8);
                await BaseCompressor.WriteThreadsToTar(outputPath, tarStream, threads);
            }

            // Step 2: Compress TAR with true XZ format using Joveler.Compression.XZ
            var xzCompressOptions = new XZCompressOptions
            {
                Level = LzmaCompLevel.Level9, // Good balance of compression and speed
                ExtremeFlag = false
            };

            using var inputStream = new FileStream(tempTarFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var xzStream = new XZStream(outputStream, xzCompressOptions);

            await inputStream.CopyToAsync(xzStream);
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "XZ compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (Exception cleanupEx)
            {
                LoggingConfiguration.Logger.Warning("Unable to delete output file {OutputPath}: {ErrorMessage}", outputPath, cleanupEx.Message);
            }

            throw;
        }
        finally
        {
            SecureIOUtilities.SafeDeleteFile(tempTarFilePath);
        }
    }

    /// <summary>
    /// Compresses email threads using streaming download to minimize memory usage.
    /// </summary>
    /// <param name="outputPath">The output file path for the compressed archive.</param>
    /// <param name="threads">A dictionary mapping thread IDs to lists of <see cref="IMessageSummary"/> objects.</param>
    /// <param name="messageFetcher">Delegate to fetch message content on-demand.</param>
    /// <param name="maxMessageSizeMB">Maximum message size in MB for streaming threshold.</param>
    public async Task CompressStreaming(string outputPath, Dictionary<ulong, List<IMessageSummary>> threads, MessageFetcher messageFetcher, int maxMessageSizeMB = 10)
    {
        var (tempFileStream, tempTarFilePath) = SecureIOUtilities.CreateSecureTempFile("xz_streaming_temp", ".tar");

        try
        {
            // Step 1: Create TAR archive using SharpZipLib
            using (tempFileStream)
            {
                using var tarStream = new TarOutputStream(tempFileStream, Encoding.UTF8);
                await BaseCompressor.WriteThreadsToTarStreaming(outputPath, tarStream, threads, messageFetcher, maxMessageSizeMB);
            }

            // Step 2: Compress TAR with true XZ format using Joveler.Compression.XZ
            // Wrap in explicit scope to ensure streams are disposed before finally block
            {
                var xzCompressOptions = new XZCompressOptions
                {
                    Level = LzmaCompLevel.Level9, // Good balance of compression and speed
                    ExtremeFlag = false
                };

                using var inputStream = new FileStream(tempTarFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var xzStream = new XZStream(outputStream, xzCompressOptions);

                await inputStream.CopyToAsync(xzStream);
            } // Streams disposed here, before finally block
        }
        catch (Exception ex)
        {
            ErrorHandler.Handle(ErrorCategory.Compression,
                "XZ streaming compression failed",
                ex,
                $"Output file: {outputPath}");
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch (Exception cleanupEx)
            {
                LoggingConfiguration.Logger.Warning("Unable to delete output file {OutputPath}: {ErrorMessage}", outputPath, cleanupEx.Message);
            }

            throw;
        }
        finally
        {
            SecureIOUtilities.SafeDeleteFile(tempTarFilePath);
        }
    }
}
