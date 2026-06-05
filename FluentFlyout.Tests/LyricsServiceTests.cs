using System;
using System.Collections.Generic;
using FluentFlyoutWPF.Classes;
using Xunit;

namespace FluentFlyout.Tests;

public class LyricsServiceTests
{
    [Fact]
    public void ParseLrcText_SingleTimestamp_ReturnsCorrectLine()
    {
        var service = new LyricsService();
        var lrcText = "[00:15.50] Hello World";

        var lines = service.ParseLrcText(lrcText);

        Assert.Single(lines);
        Assert.Equal(TimeSpan.FromSeconds(15.5), lines[0].Time);
        Assert.Equal("Hello World", lines[0].Text);
    }

    [Fact]
    public void ParseLrcText_MultipleTimestamps_ReturnsMultipleLines()
    {
        var service = new LyricsService();
        var lrcText = "[00:15.50][01:22.10] Texto de coro repetido";

        var lines = service.ParseLrcText(lrcText);

        Assert.Equal(2, lines.Count);
        
        Assert.Equal(TimeSpan.FromSeconds(15.5), lines[0].Time);
        Assert.Equal("Texto de coro repetido", lines[0].Text);

        Assert.Equal(TimeSpan.FromSeconds(82.1), lines[1].Time); // 1:22.10 is 82.1 seconds
        Assert.Equal("Texto de coro repetido", lines[1].Text);
    }

    [Fact]
    public void ParseLrcText_MultipleTimestampsWithWhitespace_ReturnsCleanText()
    {
        var service = new LyricsService();
        var lrcText = "[00:10.00] [00:20.00]   Test Line  ";

        var lines = service.ParseLrcText(lrcText);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Test Line", lines[0].Text);
        Assert.Equal("Test Line", lines[1].Text);
    }
}
