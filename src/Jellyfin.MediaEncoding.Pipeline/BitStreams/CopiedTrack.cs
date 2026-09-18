using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;

namespace Jellyfin.MediaEncoding.Pipeline.BitStreams;

/// <summary>
/// A stream that is copied rather than re-encoded, and what the client asked to receive it as.
/// </summary>
/// <param name="Codec">The codec of the source stream.</param>
/// <param name="Range">The dynamic range of the source stream.</param>
/// <param name="RangeType">The dynamic range format of the source stream.</param>
/// <param name="RequestedRangeTypes">The range formats the client said it can play.</param>
public readonly record struct CopiedTrack(
    string? Codec,
    VideoRange Range,
    VideoRangeType RangeType,
    IReadOnlyList<string> RequestedRangeTypes)
{
    /// <summary>
    /// Gets a value indicating whether the stream is H.264.
    /// </summary>
    public bool IsH264 => Matches("264") || Matches("avc");

    /// <summary>
    /// Gets a value indicating whether the stream is HEVC.
    /// </summary>
    public bool IsH265 => Matches("265") || Matches("hevc");

    /// <summary>
    /// Gets a value indicating whether the stream is AV1.
    /// </summary>
    public bool IsAv1 => Matches("av1");

    /// <summary>
    /// Gets a value indicating whether the stream is AAC.
    /// </summary>
    public bool IsAac => Matches("aac");

    /// <summary>
    /// Works out what the client cannot handle and therefore has to be stripped.
    /// </summary>
    /// <returns>The metadata to remove.</returns>
    public DynamicHdrMetadataRemoval PlanMetadataRemoval()
    {
        if (Range != VideoRange.HDR && RangeType != VideoRangeType.DOVIInvalid)
        {
            return DynamicHdrMetadataRemoval.None;
        }

        if (RequestedRangeTypes.Count == 0)
        {
            return DynamicHdrMetadataRemoval.None;
        }

        var wantsHdr10 = Requested(VideoRangeType.HDR10);
        var wantsDovi = Requested(VideoRangeType.DOVI);
        var wantsDoviWithEl = Requested(VideoRangeType.DOVIWithEL);
        var wantsDoviWithElHdr10Plus = Requested(VideoRangeType.DOVIWithELHDR10Plus);

        var removeHdr10Plus = false;

        // The client takes HDR10 but not an enhancement layer, and the stream has one.
        var removeDovi = !wantsDoviWithEl && wantsHdr10 && RangeType == VideoRangeType.DOVIWithEL;

        // A client that never claimed Dolby Vision support is left to remux the broken config itself,
        // since an HDR10 player copes with it.
        removeDovi = removeDovi || (wantsDovi && RangeType == VideoRangeType.DOVIInvalid);

        // With both an enhancement layer and HDR10+, a client that takes the layer but not the
        // combination keeps the layer and loses HDR10+, anything else loses Dolby Vision.
        if (RangeType == VideoRangeType.DOVIWithELHDR10Plus)
        {
            removeHdr10Plus = wantsDoviWithEl && !wantsDoviWithElHdr10Plus;
            removeDovi = removeDovi || !removeHdr10Plus;
        }

        if (removeDovi)
        {
            return DynamicHdrMetadataRemoval.RemoveDovi;
        }

        // A Dolby Vision player trips over HDR10+ metadata alongside it.
        removeHdr10Plus = removeHdr10Plus || (wantsDovi && RangeType == VideoRangeType.DOVIWithHDR10Plus);

        return removeHdr10Plus ? DynamicHdrMetadataRemoval.RemoveHdr10Plus : DynamicHdrMetadataRemoval.None;
    }

    private bool Matches(string fragment)
        => Codec is not null && Codec.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private bool Requested(VideoRangeType rangeType)
        => RequestedRangeTypes.Contains(rangeType.ToString(), StringComparer.OrdinalIgnoreCase);
}
