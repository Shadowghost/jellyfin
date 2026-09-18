using Xunit;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// The bitstream filters a copied stream is given, and what the client not supporting its dynamic
/// HDR metadata costs it.
/// </summary>
public class BitStreamFilterTests
{
    private const string NoFilter = "";

    public static TheoryData<string, string, string> Expected() => new()
    {
        { "h264", "-bsf:v h264_mp4toannexb", "-bsf:v h264_mp4toannexb" },
        { "avc-tagged", "-bsf:v h264_mp4toannexb", "-bsf:v h264_mp4toannexb" },
        { "aac-audio", "-bsf:a aac_adtstoasc", "-bsf:a aac_adtstoasc" },
        { "hevc-sdr", "-bsf:v hevc_mp4toannexb", "-bsf:v hevc_mp4toannexb" },
        { "hevc-hdr10-no-request", "-bsf:v hevc_mp4toannexb", "-bsf:v hevc_mp4toannexb" },
        {
            "hevc-dovi-with-el/client-hdr10",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_dovi=1",
            "-bsf:v hevc_mp4toannexb,dovi_rpu=strip=1"
        },
        { "hevc-dovi-with-el/client-dovi-with-el", "-bsf:v hevc_mp4toannexb", "-bsf:v hevc_mp4toannexb" },
        {
            "hevc-dovi-invalid/client-dovi",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_dovi=1",
            "-bsf:v hevc_mp4toannexb,dovi_rpu=strip=1"
        },
        {
            "hevc-dovi-with-hdr10plus/client-dovi",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_hdr10plus=1",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_hdr10plus=1"
        },
        {
            "hevc-dovi-with-el-hdr10plus/client-dovi-with-el",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_hdr10plus=1",
            "-bsf:v hevc_mp4toannexb,hevc_metadata=remove_hdr10plus=1"
        },
        { "av1-sdr", NoFilter, NoFilter },
        { "av1-dovi-with-el/client-hdr10", "-bsf:v av1_metadata=remove_dovi=1", "-bsf:v dovi_rpu=strip=1" }
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void GetBitStreamArgs_MatchesTheStreamAndWhatTheClientAsked(
        string name,
        string expected,
        string expectedWithoutMetadataOptions)
    {
        var testCase = BitStreamCorpus.Find(name);

        Assert.Equal(expected, testCase.BuildLegacy(TestEncodingHelper.Create()));
        Assert.Equal(expectedWithoutMetadataOptions, testCase.BuildLegacy(TestEncodingHelper.Create(false)));
    }
}
