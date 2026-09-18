using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// Holds the filter chains to what the vendor chains produced before they were replaced.
/// </summary>
public class GoldenFilterChainTests
{
    public static TheoryData<string> AllCases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in TranscodeCorpus.All.Where(Recordable))
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void EncodingHelper_ProducesTheRecordedChain(string name)
    {
        Assert.SkipWhen(GoldenFilterChains.Updating, "recording");

        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        var recorded = GoldenFilterChains.Read("expected");

        Assert.True(recorded.ContainsKey(name), $"{name} has no recorded chain");
        Assert.Equal(recorded[name], Build(testCase, TestEncodingHelper.Create()));
    }

    [Fact]
    public void Record()
    {
        Assert.SkipUnless(GoldenFilterChains.Updating, "set JELLYFIN_UPDATE_GOLDEN=1 to re-record");

        var helper = TestEncodingHelper.Create();
        var chains = new List<KeyValuePair<string, string>>();

        foreach (var testCase in TranscodeCorpus.All.Where(Recordable))
        {
            chains.Add(new(testCase.Name, Build(testCase, helper)));
        }

        GoldenFilterChains.Write(
            "expected",
            "# Recorded from the vendor chains in EncodingHelper before they were replaced.\n"
            + "# Five entries deliberately differ from what those chains produced, because they were\n"
            + "# wrong: the VideoToolbox chain dropped the 3D crop it had computed, and the AMD Vulkan\n"
            + "# chain left a rotated frame on a Vulkan surface.\n",
            chains);
    }

    /// <summary>
    /// A chain this machine could never select is recorded as its picture branch alone, which a
    /// subtitle does not fit into.
    /// </summary>
    private static bool Recordable(TranscodeCase testCase)
        => testCase.Variant == ChainVariant.Auto || testCase.Subtitle == SubtitleKind.None;

    /// <summary>
    /// A job the dispatcher settles is recorded as the whole filter parameter; a chain this machine
    /// could never select is recorded as its picture branch, which is all that can be measured.
    /// </summary>
    private static string Build(TranscodeCase testCase, MediaBrowser.Controller.MediaEncoding.EncodingHelper helper)
        => testCase.Variant == ChainVariant.Auto
            ? EncodingJobs.BuildFullParam(testCase, helper)
            : PipelineJobs.Build(testCase);
}
