using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;

namespace Shared
{
    /// <summary>
    /// Provides secure I/O operations with platform-specific permission handling.
    /// </summary>
    public static class SecureIOUtilities
    {
        /// <summary>
        /// Creates a temporary file with secure permissions that restrict access to the current user only.
        /// On Windows, uses ACLs to remove inherited permissions and grant access only to the current user.
        /// On Unix-like systems, sets file permissions to 600 (rw-------).
        /// </summary>
        /// <param name="filePath">The path where the temporary file should be created.</param>
        /// <param name="bufferSize">The buffer size for the FileStream (default: 8192).</param>
        /// <returns>A FileStream with secure permissions set.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when unable to set secure permissions.</exception>
        /// <exception cref="IOException">Thrown when file creation or permission setting fails.</exception>
        public static FileStream CreateSecureTempFile(string filePath, int bufferSize = 8192)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive.");

            // Create the file first
            FileStream? fileStream = null;
            try
            {
                fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: bufferSize);

                SetSecureFilePermissions(filePath);

                LoggingConfiguration.Logger.Debug("Created secure temporary file: {FilePath}", filePath);
                return fileStream;
            }
            catch (Exception ex)
            {
                // If anything fails, clean up the file and stream
                fileStream?.Dispose();
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch
                {
                    // Ignore cleanup errors - original exception is more important
                }

                LoggingConfiguration.Logger.Error(
                    "Failed to create secure temporary file {FilePath}: {ErrorMessage}",
                    filePath, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Creates a secure temporary file in the system temp directory with a unique name.
        /// </summary>
        /// <param name="prefix">Prefix for the temporary file name (default: "secure_temp").</param>
        /// <param name="extension">File extension including the dot (default: ".tmp").</param>
        /// <param name="bufferSize">The buffer size for the FileStream (default: 8192).</param>
        /// <returns>A tuple containing the FileStream and the full file path.</returns>
        public static (FileStream Stream, string FilePath) CreateSecureTempFile(
            string prefix = "secure_temp",
            string extension = ".tmp",
            int bufferSize = 8192)
        {
            var fileName = $"{prefix}_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(Path.GetTempPath(), fileName);

            var stream = CreateSecureTempFile(filePath, bufferSize);
            return (stream, filePath);
        }

        /// <summary>
        /// Sets secure permissions on an existing file.
        /// On Windows, uses ACLs to restrict access to the current user only.
        /// On Unix-like systems, sets file permissions to 600 (rw-------).
        /// </summary>
        /// <param name="filePath">The path to the file to secure.</param>
        /// <exception cref="UnauthorizedAccessException">Thrown when unable to set secure permissions.</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown on unsupported platforms.</exception>
        public static void SetSecureFilePermissions(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SetWindowsAclPermissions(filePath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                         RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                         RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                {
                    SetUnixPermissions(filePath);
                }
                else
                {
                    LoggingConfiguration.Logger.Warning(
                        "Unsupported platform for secure file permissions: {Platform}. File: {FilePath}",
                        RuntimeInformation.OSDescription, filePath);
                    throw new PlatformNotSupportedException(
                        $"Secure file permissions not supported on platform: {RuntimeInformation.OSDescription}");
                }
            }
            catch (Exception ex) when (!(ex is PlatformNotSupportedException))
            {
                LoggingConfiguration.Logger.Error(
                    "Failed to set secure permissions on file {FilePath}: {ErrorMessage}",
                    filePath, ex.Message);
                throw new UnauthorizedAccessException(
                    $"Unable to set secure permissions on file: {filePath}", ex);
            }
        }

        /// <summary>
        /// Sets Windows ACL permissions to restrict access to the current user only.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void SetWindowsAclPermissions(string filePath)
        {
            Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unsupported platform");
            var fileInfo = new FileInfo(filePath);
            var fileSecurity = fileInfo.GetAccessControl();
            var currentUser = WindowsIdentity.GetCurrent();

            if (currentUser?.User == null)
                throw new InvalidOperationException("Unable to determine current Windows user identity.");

            // Remove inherited permissions to prevent access by other users/groups
            fileSecurity.SetAccessRuleProtection(true, false);

            // Grant full control to current user only
            var accessRule = new FileSystemAccessRule(
                currentUser.User,
                FileSystemRights.FullControl,
                AccessControlType.Allow);
            fileSecurity.SetAccessRule(accessRule);

            // Apply the security settings
            fileInfo.SetAccessControl(fileSecurity);

            LoggingConfiguration.Logger.Debug(
                "Set Windows ACL permissions for user {UserName} on file: {FilePath}",
                currentUser.Name, filePath);
        }

        /// <summary>
        /// Sets Unix file permissions to 600 (rw-------).
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        [System.Runtime.Versioning.SupportedOSPlatform("freebsd")]
        private static void SetUnixPermissions(string filePath)
        {
            Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                         RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                         RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD), "Unsupported platform");
            // Set permissions to 600 (readable and writable by owner only)
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            LoggingConfiguration.Logger.Debug(
                "Set Unix permissions (600) on file: {FilePath}", filePath);
        }

        /// <summary>
        /// Safely deletes a file, logging any errors but not throwing exceptions.
        /// </summary>
        /// <param name="filePath">The path to the file to delete.</param>
        /// <returns>True if the file was successfully deleted or didn't exist, false if deletion failed.</returns>
        public static bool SafeDeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return true;

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    LoggingConfiguration.Logger.Debug("Deleted file: {FilePath}", filePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingConfiguration.Logger.Warning(
                    "Failed to delete file {FilePath}: {ErrorMessage}", filePath, ex.Message);
                return false;
            }
        }
    }
}