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
    private static readonly HashSet<HardwareAccelerationType> _cutOver =
    [
        HardwareAccelerationType.none,
        HardwareAccelerationType.nvenc
    ];

    private static readonly HashSet<HardwareAccelerationType> _cutOverOnLinux =
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
        Assert.SkipUnless(IsCutOver(testCase), "acceleration not cut over yet");

        var helper = LegacyEncodingHelper.Create();

        Assert.Equal(
            LegacyFilterChain.BuildFullParam(testCase, helper),
            BuildThroughPipeline(testCase, helper));
    }

    [Fact]
    public void EncodingHelper_FallsBackWhereThePackageHasNotBeenHandedTheDevice()
    {
        var helper = LegacyEncodingHelper.Create();

        foreach (var testCase in TranscodeCorpus.All.Where(c => c.Variant == ChainVariant.Auto && !IsCutOver(c)))
        {
            Assert.Equal(
                LegacyFilterChain.BuildFullParam(testCase, helper),
                BuildThroughPipeline(testCase, helper));
        }
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

        // Anything not handed over has to fall back, and anything handed over has to be built here.
        Assert.Equal(IsCutOver(testCase), built is not null);
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
        => _cutOver.Contains(testCase.Acceleration)
            || (OperatingSystem.IsLinux() && _cutOverOnLinux.Contains(testCase.Acceleration));
}
