using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// The copied streams the bitstream filter planner is measured on.
/// </summary>
internal static class BitStreamCorpus
{
    public static IReadOnlyList<BitStreamCase> All { get; } =
    [
        new() { Name = "h264", Codec = "h264" },
        new() { Name = "avc-tagged", Codec = "avc1" },
        new() { Name = "aac-audio", Codec = "aac", StreamType = MediaStreamType.Audio },
        new() { Name = "hevc-sdr", Codec = "hevc" },
        new() { Name = "hevc-hdr10-no-request", Codec = "hevc", PqTransfer = true },
        new()
        {
            Name = "hevc-dovi-with-el/client-hdr10",
            Codec = "hevc",
            DvProfile = 7,
            DvBlSignalCompatibilityId = 0,
            DoviFlags = true,
            PqTransfer = true,
            RequestedRangeTypes = "hdr10"
        },
        new()
        {
            Name = "hevc-dovi-with-el/client-dovi-with-el",
            Codec = "hevc",
            DvProfile = 7,
            DvBlSignalCompatibilityId = 0,
            DoviFlags = true,
            PqTransfer = true,
            RequestedRangeTypes = "doviwithel"
        },
        new()
        {
            Name = "hevc-dovi-invalid/client-dovi",
            Codec = "hevc",
            DvProfile = 7,
            DvBlSignalCompatibilityId = 0,
            DoviFlags = true,
            RequestedRangeTypes = "dovi"
        },
        new()
        {
            Name = "hevc-dovi-with-hdr10plus/client-dovi",
            Codec = "hevc",
            DvProfile = 8,
            DvBlSignalCompatibilityId = 1,
            DoviFlags = true,
            Hdr10Plus = true,
            PqTransfer = true,
            RequestedRangeTypes = "dovi"
        },
        new()
        {
            Name = "hevc-dovi-with-el-hdr10plus/client-dovi-with-el",
            Codec = "hevc",
            DvProfile = 7,
            DvBlSignalCompatibilityId = 0,
            DoviFlags = true,
            Hdr10Plus = true,
            PqTransfer = true,
            RequestedRangeTypes = "doviwithel"
        },
        new() { Name = "av1-sdr", Codec = "av1" },
        new()
        {
            Name = "av1-dovi-with-el/client-hdr10",
            Codec = "av1",
            DvProfile = 7,
            DvBlSignalCompatibilityId = 0,
            DoviFlags = true,
            PqTransfer = true,
            RequestedRangeTypes = "hdr10"
        }
    ];

    public static BitStreamCase Find(string name)
        => All.Single(c => string.Equals(c.Name, name, System.StringComparison.Ordinal));
}
