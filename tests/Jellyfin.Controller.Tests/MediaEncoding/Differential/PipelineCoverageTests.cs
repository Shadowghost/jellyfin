using System;
using System.Linq;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Holds the package to building every job, since nothing else builds one any more.
/// </summary>
public class PipelineCoverageTests
{
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
    public void EncodingHelper_BuildsEveryJobWithThePipeline(string name)
    {
        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        var built = LegacyEncodingHelper.Create().GetPipelineVidFilterChain(
            EncodingJobs.BuildState(testCase),
            EncodingJobs.BuildOptions(testCase),
            EncodingJobs.OutputCodecName(testCase));

        Assert.NotNull(built);
    }
}
