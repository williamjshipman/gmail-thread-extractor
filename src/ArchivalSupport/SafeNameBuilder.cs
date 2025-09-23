using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArchivalSupport;

internal static class SafeNameBuilder
{
    private const int MaxSegmentLength = 80;
    private const int MaxFileNameLength = 120;

    private static readonly HashSet<char> InvalidCharacters;

    static SafeNameBuilder()
    {
        InvalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '/',
            '\\'
        };
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
        var safeSubject = Sanitize(subject, "thread");
        return $"{threadId}_{safeSubject}";
    }

    public static string BuildMessageFileName(string uniqueId, string? subject, string dateSegment)
    {
        var safeUid = Sanitize(uniqueId, "uid", enforceLength: false);
        var safeDate = Sanitize(dateSegment, "date");
        var safeSubject = Sanitize(subject, string.Empty);

        var core = $"{safeUid}_{safeDate}";

        if (!string.IsNullOrEmpty(safeSubject))
        {
            var available = MaxFileNameLength - core.Length - 1;
            if (available > 0)
            {
                if (safeSubject.Length > available)
                {
                    safeSubject = safeSubject[..available];
                }

                core = $"{core}_{safeSubject}";
            }
        }

        if (core.Length > MaxFileNameLength)
        {
            core = core[..MaxFileNameLength];
        }

        return $"{core}.eml";
    }
}
