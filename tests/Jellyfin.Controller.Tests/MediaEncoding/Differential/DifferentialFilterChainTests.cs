using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Measures the pipeline package against the legacy chains over a corpus of transcodes.
/// </summary>
/// <remarks>
/// An acceleration type becomes a gate once it is listed in <see cref="_cutOver"/>, which is how a
/// vendor is handed over one at a time. <see cref="Report"/> keeps the remaining distance visible
/// for the ones that are not.
/// </remarks>
public class DifferentialFilterChainTests
{
    private static readonly HashSet<HardwareAccelerationType> _cutOver =
    [
        HardwareAccelerationType.none,
        HardwareAccelerationType.nvenc
    ];

    /// <summary>
    /// The types whose legacy chain only reaches its hardware path on one operating system. Off it,
    /// <see cref="EncodingHelper"/> quietly falls back to the software chain and the comparison
    /// would prove nothing.
    /// </summary>
    /// <summary>
    /// The vendor chains measured directly rather than through the dispatcher, so the operating
    /// system this runs on does not matter.
    /// </summary>
    /// <summary>
    /// The acceleration types whose subtitle overlay is reproduced as well as their picture chain.
    /// </summary>
    private static readonly HashSet<HardwareAccelerationType> _subtitlesCutOver =
    [
        HardwareAccelerationType.none,
        HardwareAccelerationType.qsv,
        HardwareAccelerationType.nvenc,
        HardwareAccelerationType.vaapi,
        HardwareAccelerationType.rkmpp,
        HardwareAccelerationType.amf,
        HardwareAccelerationType.videotoolbox
    ];

    private static readonly HashSet<ChainVariant> _cutOverVariants =
    [
        ChainVariant.VideoToolbox,
        ChainVariant.AmdD3d11,
        ChainVariant.QsvD3d11,
        ChainVariant.VaapiIntelFull,
        ChainVariant.VaapiAmdFull
    ];

    /// <summary>
    /// Cases the package deliberately does not reproduce, with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> _ungated = new(StringComparer.Ordinal)
    {
        ["vt/full-side-by-side"] = "the legacy VideoToolbox chain drops the 3D crop it computed",
        ["vt/half-side-by-side-downscale"] = "the legacy VideoToolbox chain drops the 3D crop it computed",
        ["vaapi-amd/rotate-90"] = "the legacy AMD Vulkan chain leaves a rotated frame on Vulkan",
        ["vaapi-amd/rotate-180"] = "the legacy AMD Vulkan chain leaves a rotated frame on Vulkan",
        ["vaapi-amd/rotate-90-and-downscale"] = "the legacy AMD Vulkan chain leaves a rotated frame on Vulkan"
    };

    private static readonly HashSet<HardwareAccelerationType> _cutOverOnLinux =
    [
        HardwareAccelerationType.vaapi,
        HardwareAccelerationType.qsv,
        HardwareAccelerationType.rkmpp
    ];

    public static TheoryData<string> AllCases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in TranscodeCorpus.All)
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacyChain(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        // A case that burns in a subtitle is measured as a whole graph instead.
        Assert.SkipUnless(testCase.Subtitle == SubtitleKind.None, "measured as a graph");
        Assert.SkipUnless(IsGated(testCase), GateReason(testCase));

        var legacy = LegacyFilterChain.Build(testCase, LegacyEncodingHelper.Create());
        var pipeline = PipelineFilterChain.Build(testCase);

        Assert.Equal(legacy, pipeline);
    }

    private static bool IsGated(TranscodeCase testCase)
    {
        if (_ungated.ContainsKey(testCase.Name))
        {
            return false;
        }

        return testCase.Variant == ChainVariant.Auto
            ? _cutOver.Contains(testCase.Acceleration)
                || (OperatingSystem.IsLinux() && _cutOverOnLinux.Contains(testCase.Acceleration))
            : _cutOverVariants.Contains(testCase.Variant);
    }

    private static string GateReason(TranscodeCase testCase)
        => _ungated.TryGetValue(testCase.Name, out var reason) ? reason : "not cut over yet";

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacySubtitleGraph(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        Assert.SkipUnless(testCase.Subtitle != SubtitleKind.None, "no subtitle to burn in");

        // The whole parameter comes from the dispatcher, which a directly measured chain bypasses.
        Assert.SkipUnless(
            testCase.Variant == ChainVariant.Auto
            && IsGated(testCase)
            && _subtitlesCutOver.Contains(testCase.Acceleration),
            "subtitles not cut over yet");

        Assert.Equal(
            LegacyFilterChain.BuildFullParam(testCase, LegacyEncodingHelper.Create()),
            PipelineFilterChain.BuildFullParam(testCase));
    }

    [Fact]
    public void ReportSubtitleGraphs()
    {
        var helper = LegacyEncodingHelper.Create();
        var report = new StringBuilder();
        var matching = 0;
        var total = 0;

        foreach (var testCase in TranscodeCorpus.All
            .Where(c => c.Subtitle != SubtitleKind.None && c.Variant == ChainVariant.Auto))
        {
            report.Append("-- ").AppendLine(testCase.Name);
            report.Append("   legacy:   ").AppendLine(LegacyFilterChain.BuildFullParam(testCase, helper));
            report.Append("   pipeline: ").AppendLine(PipelineFilterChain.BuildFullParam(testCase));
            total++;
            matching += string.Equals(
                LegacyFilterChain.BuildFullParam(testCase, helper),
                PipelineFilterChain.BuildFullParam(testCase),
                StringComparison.Ordinal) ? 1 : 0;
        }

        report.Append("subtitle graphs matching ").Append(matching).Append(" of ").Append(total).AppendLine();

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    [Fact]
    public void ReportInputArgs()
    {
        var helper = LegacyEncodingHelper.Create();
        var report = new StringBuilder();
        var matching = 0;

        foreach (var testCase in TranscodeCorpus.All)
        {
            var legacy = LegacyFilterChain.BuildInputArgs(testCase, helper);
            var pipeline = PipelineFilterChain.BuildInputArgs(testCase);
            var matches = string.Equals(legacy, pipeline, StringComparison.Ordinal);
            matching += matches ? 1 : 0;

            report.Append(matches ? "== " : "!= ").AppendLine(testCase.Name);
            report.Append("   legacy:   ").AppendLine(legacy);
            report.Append("   pipeline: ").AppendLine(pipeline);
        }

        report.Append("input args matching ").Append(matching)
            .Append(" of ").Append(TranscodeCorpus.All.Count(c => c.Subtitle == SubtitleKind.None)).AppendLine();

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacyInputArgs(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        // The input arguments come from the dispatcher, which a directly measured chain bypasses.
        Assert.SkipUnless(testCase.Variant == ChainVariant.Auto && IsGated(testCase), GateReason(testCase));

        Assert.Equal(
            LegacyFilterChain.BuildInputArgs(testCase, LegacyEncodingHelper.Create()),
            PipelineFilterChain.BuildInputArgs(testCase));
    }

    [Fact]
    public void Report()
    {
        var helper = LegacyEncodingHelper.Create();
        var report = new StringBuilder();
        var matching = new List<string>();

        foreach (var testCase in TranscodeCorpus.All.Where(c => c.Subtitle == SubtitleKind.None))
        {
            var legacy = LegacyFilterChain.Build(testCase, helper);
            var pipeline = PipelineFilterChain.Build(testCase);
            var matches = string.Equals(legacy, pipeline, StringComparison.Ordinal);

            if (matches)
            {
                matching.Add(testCase.Name);
            }

            report.Append(matches ? "== " : "!= ").AppendLine(testCase.Name);
            report.Append("   legacy:   ").AppendLine(legacy);
            report.Append("   pipeline: ").AppendLine(pipeline);
        }

        report.Append("matching ").Append(matching.Count)
            .Append(" of ").Append(TranscodeCorpus.All.Count(c => c.Subtitle == SubtitleKind.None)).AppendLine();
        foreach (var name in matching)
        {
            report.Append("  ").AppendLine(name);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }
}
