using MailKit;
using MimeKit;

namespace ArchivalSupport
{
    /// <summary>
    /// Provides methods to convert MimeMessage objects and their summaries into MessageBlob objects,
    /// facilitating the serialization and storage of email messages with metadata.
    /// </summary>
    public class MessageWriter
    {
        /// <summary>
        /// Convert a MimeMessage object to a MessageBlob object.
        /// Uses streaming for large messages to reduce memory consumption.
        /// The UniqueId is only available in the summary, not MimeMessage.
        /// </summary>
        /// <param name="msgSummary">IMessageSummary object giving the unique ID of the message.</param>
        /// <param name="message">The email downloaded from GMail.</param>
        /// <param name="maxSizeBytes">Maximum size in bytes to load into memory. Larger messages use streaming.</param>
        /// <returns>A new MessageBlob object.</returns>
        public static MessageBlob MessageToBlob(IMessageSummary msgSummary, MimeMessage message, long maxSizeBytes = 10 * 1024 * 1024)
        {
            var reportedSize = msgSummary?.Size.HasValue == true ? (long)msgSummary.Size.Value : -1L;

            if (reportedSize >= 0 && reportedSize <= maxSizeBytes)
            {
                using var buffer = new MemoryStream();
                message.WriteTo(buffer);
                return new MessageBlob(
                    msgSummary?.UniqueId.ToString() ?? "unknown",
                    buffer.ToArray(),
                    message.Subject ?? string.Empty,
                    message.From?.ToString() ?? string.Empty,
                    message.To?.ToString() ?? string.Empty,
                    message.Date.UtcDateTime);
            }

            if (reportedSize > maxSizeBytes)
            {
                Console.WriteLine($"Message {msgSummary?.UniqueId.ToString() ?? "unknown"} ({reportedSize:N0} bytes) will use streaming");
                return new MessageBlob(
                    msgSummary?.UniqueId.ToString() ?? "unknown",
                    async stream =>
                    {
                        message.WriteTo(stream);
                        await stream.FlushAsync();
                    },
                    reportedSize,
                    message.Subject ?? string.Empty,
                    message.From?.ToString() ?? string.Empty,
                    message.To?.ToString() ?? string.Empty,
                    message.Date.UtcDateTime);
            }

            using var probe = new MemoryStream();
            message.WriteTo(probe);

            if (probe.Length <= maxSizeBytes)
            {
                return new MessageBlob(
                    msgSummary?.UniqueId.ToString() ?? "unknown",
                    probe.ToArray(),
                    message.Subject ?? string.Empty,
                    message.From?.ToString() ?? string.Empty,
                    message.To?.ToString() ?? string.Empty,
                    message.Date.UtcDateTime);
            }

            Console.WriteLine($"Message {msgSummary?.UniqueId.ToString() ?? "unknown"} ({probe.Length:N0} bytes) will use streaming");

            return new MessageBlob(
                msgSummary?.UniqueId.ToString() ?? "unknown",
                async stream =>
                {
                    message.WriteTo(stream);
                    await stream.FlushAsync();
                },
                probe.Length,
                message.Subject ?? string.Empty,
                message.From?.ToString() ?? string.Empty,
                message.To?.ToString() ?? string.Empty,
                message.Date.UtcDateTime);
        }

        /// <summary>
        /// Convert a list of MimeMessage objects to MessageBlob objects.
        /// The list of IMessageSummary objects is used to get the UniqueId of each
        /// message because this field isn't stored in MimeMessage.
        /// </summary>
        /// <param name="messages">The list of messages downloaded from
        /// GMail.</param>
        /// <param name="msgSummaries">The list of IMessageSummary objects used to
        /// get the UniqueId of each message.</param>
        /// <returns>A list of MessageBlob objects corresponding to the provided
        /// messages.</returns>
        public static List<MessageBlob> MessagesToBlobs(
            List<MimeMessage> messages,
            List<IMessageSummary> msgSummaries)
        {
            var messageBlobs = new List<MessageBlob>();
            foreach (var (message, msgSummary) in messages.Zip(msgSummaries, (m, s) => (m, s)))
            {
                messageBlobs.Add(MessageToBlob(msgSummary, message));
            }
            return messageBlobs;
        }
    }

    /// <summary>
    /// Represents an email message with metadata and its serialized content.
    /// Supports both in-memory (Blob) and streaming (StreamFunc) approaches for large messages.
    /// </summary>
    public class MessageBlob
    {
        public string UniqueId { get; set; }
        public byte[]? Blob { get; set; }
        public Func<Stream, Task>? StreamFunc { get; set; }
        public string Subject { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public DateTime Date { get; set; }
        public long Size { get; set; }
        public bool IsStreaming => StreamFunc != null;

        public string DateString =>
            Date.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss");

        public string FileName => SafeNameBuilder.BuildMessageFileName(UniqueId, Subject, DateString);

        /// <summary>
        /// Construct a new MessageBlob object with in-memory data.
        /// </summary>
        /// <param name="uniqueid">The unique ID of the message.</param>
        /// <param name="blob">The message serialised to a binary blob.</param>
        /// <param name="subject">The subject of the email.</param>
        /// <param name="from">The sender of the email.</param>
        /// <param name="to">The recipient of the email.</param>
        /// <param name="date">The date the email was sent.</param>
        public MessageBlob(string uniqueid, byte[] blob, string subject, string from, string to, DateTime date)
        {
            UniqueId = uniqueid;
            Blob = blob;
            Subject = subject;
            From = from;
            To = to;
            Date = date;
            Size = blob.Length;
        }

        /// <summary>
        /// Construct a new MessageBlob object with streaming data.
        /// </summary>
        /// <param name="uniqueid">The unique ID of the message.</param>
        /// <param name="streamFunc">Function to write the message content to a stream.</param>
        /// <param name="size">The size of the message content.</param>
        /// <param name="subject">The subject of the email.</param>
        /// <param name="from">The sender of the email.</param>
        /// <param name="to">The recipient of the email.</param>
        /// <param name="date">The date the email was sent.</param>
        public MessageBlob(string uniqueid, Func<Stream, Task> streamFunc, long size, string subject, string from, string to, DateTime date)
        {
            UniqueId = uniqueid;
            StreamFunc = streamFunc;
            Size = size;
            Subject = subject;
            From = from;
            To = to;
            Date = date;
        }

        /// <summary>
        /// Override ToString method to provide a string representation of the
        /// MessageBlob object.
        /// </summary>
        /// <returns>String representation.</returns>
        public override string ToString()
        {
            return $"Subject: {Subject}\n" +
                   $"From: {From}\n" +
                   $"To: {To}\n" +
                   $"Date: {Date}\n" +
                   $"Size: {Size} bytes\n" +
                   $"UID: {UniqueId}\n";
        }
    }
}
