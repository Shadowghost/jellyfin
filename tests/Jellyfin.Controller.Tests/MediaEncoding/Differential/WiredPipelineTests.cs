using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Measures the pipeline package as the server actually reaches it, by asking
/// <see cref="MediaBrowser.Controller.MediaEncoding.EncodingHelper"/> for the same job twice with the
/// switch off and on.
/// </summary>
public class WiredPipelineTests
{
    /// <summary>
    /// The acceleration types the package builds wherever the server runs.
    /// </summary>
    private static readonly HashSet<HardwareAccelerationType> _everywhere =
    [
        HardwareAccelerationType.none,
        HardwareAccelerationType.nvenc
    ];

    /// <summary>
    /// The types whose devices only exist on one operating system, which the package follows.
    /// </summary>
    private static readonly HashSet<HardwareAccelerationType> _onLinux =
    [
        HardwareAccelerationType.vaapi,
        HardwareAccelerationType.qsv,
        HardwareAccelerationType.rkmpp
    ];

    public static TheoryData<string> AllCases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in TranscodeCorpus.All.Where(c => c.Variant == ChainVariant.Auto))
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EncodingHelper_BuildsTheSameChainThroughThePipeline(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        var helper = LegacyEncodingHelper.Create();

        Assert.Equal(
            LegacyFilterChain.BuildFullParam(testCase, helper),
            BuildThroughPipeline(testCase, helper));
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EncodingHelper_ActuallyTakesThePipelineRouteWhereItIsCutOver(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        var helper = LegacyEncodingHelper.Create();
        var options = LegacyFilterChain.BuildOptions(testCase);

        var built = helper.GetPipelineVidFilterChain(
            LegacyFilterChain.BuildState(testCase),
            options,
            LegacyFilterChain.OutputCodecName(testCase));

        // Every job is built here now; nothing falls back to a vendor chain.
        Assert.NotNull(built);
    }

    [Fact]
    public void Report()
    {
        var helper = LegacyEncodingHelper.Create();
        var report = new StringBuilder();
        var matching = 0;
        var cases = TranscodeCorpus.All.Where(c => c.Variant == ChainVariant.Auto).ToList();

        foreach (var testCase in cases)
        {
            var legacy = LegacyFilterChain.BuildFullParam(testCase, helper);
            var wired = BuildThroughPipeline(testCase, helper);
            var matches = string.Equals(legacy, wired, StringComparison.Ordinal);
            matching += matches ? 1 : 0;

            report.Append(matches ? "== " : "!= ").AppendLine(testCase.Name);
            if (!matches)
            {
                report.Append("   legacy: ").AppendLine(legacy);
                report.Append("   wired:  ").AppendLine(wired);
            }
        }

        report.Append("wired matching ").Append(matching).Append(" of ").Append(cases.Count).AppendLine();
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    /// <summary>
    /// The same call with the pipeline package switched on, which is how the server reaches it.
    /// </summary>
    private static string BuildThroughPipeline(TranscodeCase testCase, EncodingHelper helper)
    {
        var options = LegacyFilterChain.BuildOptions(testCase);
        options.EnableFilterChainBuilder = true;

        return helper
            .GetVideoProcessingFilterParam(
                LegacyFilterChain.BuildState(testCase),
                options,
                LegacyFilterChain.OutputCodecName(testCase))
            .Trim();
    }

    private static bool IsCutOver(TranscodeCase testCase)
        => _everywhere.Contains(testCase.Acceleration)
            || (OperatingSystem.IsLinux() && _onLinux.Contains(testCase.Acceleration))
            || (OperatingSystem.IsWindows() && testCase.Acceleration == HardwareAccelerationType.amf)
            || (OperatingSystem.IsMacOS() && testCase.Acceleration == HardwareAccelerationType.videotoolbox);
}
