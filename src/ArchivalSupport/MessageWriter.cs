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
        /// The UniqueId is only available in the summary, not MimeMessage.
        /// </summary>
        /// <param name="msgSummary">IMessageSummary object giving the unique ID of
        /// the message.</param>
        /// <param name="message">The email downloaded from GMail.</param>
        /// <returns>A new MessageBlob object.</returns>
        public static MessageBlob MessageToBlob(IMessageSummary msgSummary, MimeMessage message)
        {
            using (var memoryStream = new MemoryStream())
            {
                message.WriteTo(memoryStream);
                return new MessageBlob(
                    msgSummary.UniqueId.ToString(), // UniqueId is only available in the summary, not MimeMessage.
                    memoryStream.ToArray(),
                    message.Subject,
                    message.From.ToString(),
                    message.To.ToString(),
                    message.Date.UtcDateTime);
            }
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
    /// Stores the unique identifier, subject, sender, recipient, date, and the raw message data.
    /// </summary>
    public class MessageBlob
    {
        public string UniqueId { get; set; }
        public byte[] Blob { get; set; }
        public string Subject { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public DateTime Date { get; set; }
        public string DateString =>
            Date.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss");
        public long Size => (long)Blob.Length;

        public string FileName
        {
            get
            {
                return $"{From}_{DateString}.eml";
            }
        }

        /// <summary>
        /// Construct a new MessageBlob object.
        /// </summary>
        /// <param name="uniqueid">The unique ID of the message.</param>
        /// <param name="blob">The message serialised to a binary blog.</param>
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