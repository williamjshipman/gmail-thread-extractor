using System;
using System.Linq;
using ArchivalSupport;
using Xunit;

namespace ArchivalSupport.Tests;

public class SafeNameBuilderTests
{
    private static readonly char[] ForbiddenCharacters = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    [Fact]
    public void BuildThreadDirectoryName_ReplacesInvalidCharacters()
    {
        var result = SafeNameBuilder.BuildThreadDirectoryName(1234567890123456789UL, "Invoice: Q3/2024? Draft");

        Assert.StartsWith("1234567890123456789_", result);
        Assert.True(result.Length <= 100, "Thread directory names must remain within tar name limits.");
        Assert.All(ForbiddenCharacters, ch => Assert.DoesNotContain(ch, result));
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void BuildThreadDirectoryName_TrimsSubjectToFitTarLimit()
    {
        var longSubject = new string('a', 200);
        var result = SafeNameBuilder.BuildThreadDirectoryName(1, longSubject);

        Assert.True(result.Length <= 100);
        Assert.StartsWith("1_", result);
    }

    [Fact]
    public void BuildMessageFileName_ReplacesReservedCharactersAndAppendsExtension()
    {
        var fileName = SafeNameBuilder.BuildMessageFileName("unique:../id", "Quarterly <Update>", "2024-12-31_23-59-59");

        Assert.EndsWith(".eml", fileName);
        Assert.True(fileName.Length <= 120);
        Assert.DoesNotContain("..", fileName);
        Assert.All(ForbiddenCharacters, ch => Assert.DoesNotContain(ch, fileName));
    }

    [Fact]
    public void BuildMessageFileName_TrimsSubjectWhenExceedingLimit()
    {
        var longSubject = new string('b', 200);
        var fileName = SafeNameBuilder.BuildMessageFileName(new string('u', 200), longSubject, "2025-01-01_00-00-00");

        Assert.True(fileName.Length <= 120);
        Assert.StartsWith(new string('u', 80) + "_2025-01-01_00-00-00", fileName);
    }
}
