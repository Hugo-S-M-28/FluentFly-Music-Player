using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluentFlyoutWPF.Classes;

public struct LyricWord
{
    public TimeSpan Time { get; set; }
    public string Text { get; set; }
}

public struct LyricLine
{
    public TimeSpan Time { get; set; }
    public string Text { get; set; }
    public List<LyricWord>? Words { get; set; }
}

public class LyricsService
{
    private static readonly Regex LyricsRegex = new(@"^\[(?<time>\d{2}:\d{2}(?:\.\d{1,3})?)\](?<text>.*)$", RegexOptions.Compiled);

    public List<LyricLine> ParseLrc(string filePath)
    {
        if (!File.Exists(filePath)) return new List<LyricLine>();

        try
        {
            var text = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            return ParseLrcText(text);
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to parse .lrc file");
            return new List<LyricLine>();
        }
    }

    private static readonly Regex WordTimestampRegex = new(@"<(?<time>\d{2}:\d{2}(?:\.\d{1,3})?)>(?<text>[^<]*)", RegexOptions.Compiled);

    public List<LyricLine> ParseLrcText(string lrcText)
    {
        var lyrics = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrcText)) return lyrics;

        var lines = lrcText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = LyricsRegex.Match(line.Trim());
            if (match.Success)
            {
                if (TimeSpan.TryParse("00:" + match.Groups["time"].Value, out var time))
                {
                    string rawText = match.Groups["text"].Value.Trim();
                    var words = new List<LyricWord>();

                    // Parse enhanced LRC word timings if present
                    var wordMatches = WordTimestampRegex.Matches(rawText);
                    if (wordMatches.Count > 0)
                    {
                        foreach (Match wordMatch in wordMatches)
                        {
                            if (TimeSpan.TryParse("00:" + wordMatch.Groups["time"].Value, out var wordTime))
                            {
                                words.Add(new LyricWord
                                {
                                    Time = wordTime,
                                    Text = wordMatch.Groups["text"].Value
                                });
                            }
                        }
                        
                        // Strip tags for the display text
                        rawText = WordTimestampRegex.Replace(rawText, "$2");
                    }

                    lyrics.Add(new LyricLine
                    {
                        Time = time,
                        Text = rawText,
                        Words = words.Count > 0 ? words : null
                    });
                }
            }
        }
        return lyrics.OrderBy(l => l.Time).ToList();
    }

    public void SaveLrc(string filePath, List<LyricLine> lyrics)
    {
        try
        {
            var lines = lyrics.OrderBy(l => l.Time)
                             .Select(l => $"[{FormatTime(l.Time)}]{l.Text}");
            File.WriteAllLines(filePath, lines, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to save .lrc file");
            throw;
        }
    }

    public string? FindLrcFile(string audioFilePath)
    {
        var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
        return File.Exists(lrcPath) ? lrcPath : null;
    }

    private string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
    }
}
