using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// Measures the pipeline package's bitstream filters against the legacy ones.
/// </summary>
/// <remarks>
/// Bitstream filters never depend on the hardware, so they are gated everywhere from the start.
/// </remarks>
public class DifferentialBitStreamTests
{
    public static TheoryData<string> AllCases()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in BitStreamCorpus.All)
        {
            data.Add(testCase.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacyBitStreamArgs(string name)
    {
        var testCase = BitStreamCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        Assert.Equal(testCase.BuildLegacy(LegacyEncodingHelper.Create()), testCase.BuildPipeline());
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Pipeline_ReproducesTheLegacyBitStreamArgs_WithoutTheMetadataFilterOptions(string name)
    {
        var testCase = BitStreamCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        Assert.Equal(
            testCase.BuildLegacy(LegacyEncodingHelper.Create(false)),
            testCase.BuildPipeline(false));
    }

    [Fact]
    public void Report()
    {
        var helper = LegacyEncodingHelper.Create();
        var report = new StringBuilder();

        foreach (var testCase in BitStreamCorpus.All)
        {
            var legacy = testCase.BuildLegacy(helper);
            var pipeline = testCase.BuildPipeline();

            report.Append(string.Equals(legacy, pipeline, StringComparison.Ordinal) ? "== " : "!= ")
                .AppendLine(testCase.Name);
            report.Append("   legacy:   ").AppendLine(legacy);
            report.Append("   pipeline: ").AppendLine(pipeline);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }
}
