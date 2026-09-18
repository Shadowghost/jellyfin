using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// Holds the devices and decoder a job asks for to what the encoding helper produced before the
/// pipeline package took them over.
/// </summary>
public class InputArgumentTests
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
    public void EncodingHelper_ProducesTheRecordedInputArguments(string name)
    {
        Assert.SkipWhen(GoldenFilterChains.Updating, "recording");
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the devices are only reachable on Linux");

        var testCase = TranscodeCorpus.All.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        var recorded = GoldenFilterChains.Read("expected-input");

        Assert.True(recorded.ContainsKey(name), $"{name} has no recorded arguments");
        Assert.Equal(recorded[name], EncodingJobs.BuildInputArgs(testCase, TestEncodingHelper.Create()));
    }

    [Fact]
    public void Record()
    {
        Assert.SkipUnless(GoldenFilterChains.Updating, "set JELLYFIN_UPDATE_GOLDEN=1 to re-record");

        var helper = TestEncodingHelper.Create();

        GoldenFilterChains.Write(
            "expected-input",
            "# The devices and decoder each job asks for, recorded on Linux.\n",
            TranscodeCorpus.All
                .Where(c => c.Variant == ChainVariant.Auto)
                .Select(c => new KeyValuePair<string, string>(c.Name, EncodingJobs.BuildInputArgs(c, helper))));
    }

    [Fact]
    public void Report()
    {
        var helper = TestEncodingHelper.Create();
        var report = new StringBuilder();
        var matching = 0;
        var cases = TranscodeCorpus.All.Where(c => c.Variant == ChainVariant.Auto).ToList();

        foreach (var testCase in cases)
        {
            var helperArgs = EncodingJobs.BuildInputArgs(testCase, helper);
            var pipeline = PipelineJobs.BuildInputArgs(testCase);
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
