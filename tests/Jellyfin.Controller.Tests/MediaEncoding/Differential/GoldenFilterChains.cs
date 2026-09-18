using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// The filter chains the corpus is expected to produce, recorded on disk.
/// </summary>
/// <remarks>
/// They were taken from the vendor chains in <see cref="EncodingHelper"/> while those still existed,
/// so they carry that behaviour forward once it is gone. Regenerate by setting
/// <c>JELLYFIN_UPDATE_GOLDEN=1</c>, and read the diff before keeping it.
/// </remarks>
internal static class GoldenFilterChains
{
    private const string Separator = " => ";

    public static string Path { get; } = System.IO.Path.Combine(
        AppContext.BaseDirectory,
        "Test Data",
        "FilterChains",
        "expected.txt");

    public static bool Updating => string.Equals(
        Environment.GetEnvironmentVariable("JELLYFIN_UPDATE_GOLDEN"),
        "1",
        StringComparison.Ordinal);

    public static IReadOnlyDictionary<string, string> Read()
    {
        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path))
        {
            var at = line.IndexOf(Separator, StringComparison.Ordinal);
            if (at > 0)
            {
                recorded[line[..at]] = line[(at + Separator.Length)..];
            }
        }

        return recorded;
    }

    public static void Write(IEnumerable<KeyValuePair<string, string>> chains)
    {
        var contents = new StringBuilder();
        foreach (var (name, chain) in chains.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            contents.Append(name).Append(Separator).AppendLine(chain);
        }

        var source = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Test Data",
            "FilterChains",
            "expected.txt");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(source)!);
        File.WriteAllText(source, contents.ToString());
    }
}
