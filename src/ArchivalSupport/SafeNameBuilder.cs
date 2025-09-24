using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArchivalSupport;

internal static class SafeNameBuilder
{
    private const int MaxSegmentLength = 60;
    private const int MaxTarNameLength = 100; // USTAR name field limit
    private static readonly HashSet<char> InvalidCharacters;

    static SafeNameBuilder()
    {
        var invalid = new HashSet<char>
        {
            '/', '\\', ':', '*', '?', '"', '<', '>', '|'
        };

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            invalid.Add(ch);
        }

        InvalidCharacters = invalid;
    }

    private static string Sanitize(string? value, string fallback, bool enforceLength = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            if (InvalidCharacters.Contains(ch) || char.IsControl(ch))
            {
                builder.Append('_');
                continue;
            }

            builder.Append(ch);
        }

        var sanitized = builder.ToString();

        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", "_", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim(' ', '.', '_');

        if (sanitized.Length == 0)
        {
            sanitized = fallback;
        }

        if (enforceLength && sanitized.Length > MaxSegmentLength)
        {
            sanitized = sanitized[..MaxSegmentLength];
        }

        return sanitized;
    }

    public static string BuildThreadDirectoryName(ulong threadId, string? subject)
    {
        var threadSegment = threadId.ToString();
        var safeSubject = Sanitize(subject, "thread");

        const string delimiter = "_";
        var bareMaxLength = MaxTarNameLength - 1; // reserve space for the trailing slash added to tar entries
        var availableForSubject = bareMaxLength - threadSegment.Length - delimiter.Length;

        string name;
        if (availableForSubject <= 0)
        {
            name = threadSegment.Length > bareMaxLength ? threadSegment[..bareMaxLength] : threadSegment;
        }
        else
        {
            if (safeSubject.Length > availableForSubject)
            {
                safeSubject = safeSubject[..availableForSubject];
            }

            name = $"{threadSegment}{delimiter}{safeSubject}";
        }

        if (name.Length > bareMaxLength)
        {
            name = name[..bareMaxLength];
        }

        return name;
    }

    public static string BuildMessageFileName(string uniqueId, string? subject, string dateSegment, string? from = null)
    {
        const string Extension = ".eml";
        var coreMaxLength = MaxTarNameLength - Extension.Length;

        var safeUid = Sanitize(uniqueId, "uid", enforceLength: false);
        var safeDate = Sanitize(dateSegment, "date");
        var safeFrom = Sanitize(ExtractSenderName(from), string.Empty);
        var safeSubject = Sanitize(subject, string.Empty);

        if (safeUid.Length > MaxSegmentLength)
        {
            safeUid = safeUid[..MaxSegmentLength];
        }

        // Build core with UID and date
        var core = $"{safeUid}_{safeDate}";
        var remaining = coreMaxLength - core.Length;

        // Add sender name if there's space and it exists
        if (!string.IsNullOrEmpty(safeFrom) && remaining > 1)
        {
            remaining -= 1; // Account for underscore
            if (remaining > 0)
            {
                if (safeFrom.Length > remaining)
                {
                    safeFrom = safeFrom[..remaining];
                }
                core = $"{core}_{safeFrom}";
                remaining = coreMaxLength - core.Length;
            }
        }

        // Add subject if there's space and it exists
        if (!string.IsNullOrEmpty(safeSubject) && remaining > 1)
        {
            remaining -= 1; // Account for underscore
            if (remaining > 0)
            {
                if (safeSubject.Length > remaining)
                {
                    safeSubject = safeSubject[..remaining];
                }
                core = $"{core}_{safeSubject}";
            }
        }

        if (core.Length > coreMaxLength)
        {
            core = core[..coreMaxLength];
        }

        return $"{core}{Extension}";
    }

    /// <summary>
    /// Extracts the sender name from an email address or name/email combination.
    /// </summary>
    /// <param name="from">The from field which could be "Name &lt;email@domain.com&gt;" or just "email@domain.com"</param>
    /// <returns>The extracted name or email username if no name is present</returns>
    private static string ExtractSenderName(string? from)
    {
        if (string.IsNullOrWhiteSpace(from))
            return string.Empty;

        // Handle format: "Display Name <email@domain.com>"
        var angleStart = from.IndexOf('<');
        if (angleStart > 0)
        {
            var displayName = from[..angleStart].Trim();
            if (!string.IsNullOrEmpty(displayName))
            {
                // Remove quotes if present
                if (displayName.StartsWith('"') && displayName.EndsWith('"'))
                {
                    displayName = displayName[1..^1];
                }
                return displayName;
            }
        }

        // Handle just email address: extract username part
        var atIndex = from.IndexOf('@');
        if (atIndex > 0)
        {
            return from[..atIndex];
        }

        // Return as-is if we can't parse it
        return from;
    }
}
