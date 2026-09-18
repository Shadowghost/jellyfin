using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Measures the pipeline package's device and decoder arguments against the ones
/// <see cref="MediaBrowser.Controller.MediaEncoding.EncodingHelper"/> still builds.
/// </summary>
/// <remarks>
/// The filter chains no longer have a second implementation to compare against, so they are held to
/// <see cref="GoldenFilterChains"/> instead. The input arguments still have one.
/// </remarks>
public class DifferentialInputArgsTests
{
    private static readonly HashSet<HardwareAccelerationType> _onLinux =
    [
        HardwareAccelerationType.vaapi,
        HardwareAccelerationType.qsv,
        HardwareAccelerationType.rkmpp,
        HardwareAccelerationType.nvenc,
        HardwareAccelerationType.none
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
    public void Pipeline_ReproducesTheInputArguments(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && _onLinux.Contains(testCase.Acceleration),
            "the devices of this type are not reachable here");

        Assert.Equal(
            EncodingJobs.BuildInputArgs(testCase, LegacyEncodingHelper.Create()),
            PipelineFilterChain.BuildInputArgs(testCase));
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
            var helperArgs = EncodingJobs.BuildInputArgs(testCase, helper);
            var pipeline = PipelineFilterChain.BuildInputArgs(testCase);
            var matches = string.Equals(helperArgs, pipeline, StringComparison.Ordinal);
            matching += matches ? 1 : 0;

            report.Append(matches ? "== " : "!= ").AppendLine(testCase.Name);
            if (!matches)
            {
                report.Append("   helper:   ").AppendLine(helperArgs);
                report.Append("   pipeline: ").AppendLine(pipeline);
            }
        }

        report.Append("input args matching ").Append(matching).Append(" of ").Append(cases.Count).AppendLine();
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }
}
