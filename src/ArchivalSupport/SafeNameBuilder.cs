using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArchivalSupport;

internal static class SafeNameBuilder
{
    private const int MaxSegmentLength = 80;
    private const int MaxFileNameLength = 120;
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
        var availableForSubject = MaxTarNameLength - threadSegment.Length - delimiter.Length;

        string name;
        if (availableForSubject <= 0)
        {
            name = threadSegment;
        }
        else
        {
            if (safeSubject.Length > availableForSubject)
            {
                safeSubject = safeSubject[..availableForSubject];
            }

            name = $"{threadSegment}{delimiter}{safeSubject}";
        }

        if (name.Length > MaxTarNameLength)
        {
            name = name[..MaxTarNameLength];
        }

        return name;
    }

    public static string BuildMessageFileName(string uniqueId, string? subject, string dateSegment)
    {
        const string Extension = ".eml";
        var coreMaxLength = MaxFileNameLength - Extension.Length;

        var safeUid = Sanitize(uniqueId, "uid", enforceLength: false);
        var safeDate = Sanitize(dateSegment, "date");
        var safeSubject = Sanitize(subject, string.Empty);

        if (safeUid.Length > MaxSegmentLength)
        {
            safeUid = safeUid[..MaxSegmentLength];
        }

        var core = $"{safeUid}_{safeDate}";
        var remaining = coreMaxLength - core.Length;

        if (!string.IsNullOrEmpty(safeSubject) && remaining > 1)
        {
            // Account for joining underscore.
            remaining -= 1;
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
}
