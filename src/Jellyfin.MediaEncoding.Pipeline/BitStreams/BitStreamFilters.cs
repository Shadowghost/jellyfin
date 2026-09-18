using Jellyfin.MediaEncoding.Pipeline;

namespace Jellyfin.MediaEncoding.Pipeline.BitStreams;

/// <summary>
/// The bitstream filters a copied stream needs to land in the output container, and to lose the
/// dynamic HDR metadata the client cannot play.
/// </summary>
public static class BitStreamFilters
{
    /// <summary>
    /// Works out the filters for one copied stream.
    /// </summary>
    /// <param name="stream">The stream being copied.</param>
    /// <param name="capabilities">What the installed ffmpeg was built with.</param>
    /// <returns>The argument, or <c>null</c> when the stream needs no filter.</returns>
    public static string? For(CopiedTrack stream, IPipelineCapabilities capabilities)
    {
        if (stream.IsH264)
        {
            return "-bsf:v h264_mp4toannexb";
        }

        if (stream.IsAac)
        {
            return "-bsf:a aac_adtstoasc";
        }

        if (stream.IsH265)
        {
            var filter = "-bsf:v hevc_mp4toannexb";

            return stream.PlanMetadataRemoval() switch
            {
                DynamicHdrMetadataRemoval.RemoveDovi => filter
                    + (capabilities.SupportsBitStreamFilterOption(BitStreamFilterOption.HevcMetadataRemoveDovi)
                        ? ",hevc_metadata=remove_dovi=1"
                        : ",dovi_rpu=strip=1"),
                DynamicHdrMetadataRemoval.RemoveHdr10Plus => filter + ",hevc_metadata=remove_hdr10plus=1",
                _ => filter
            };
        }

        if (stream.IsAv1)
        {
            return stream.PlanMetadataRemoval() switch
            {
                DynamicHdrMetadataRemoval.RemoveDovi =>
                    capabilities.SupportsBitStreamFilterOption(BitStreamFilterOption.Av1MetadataRemoveDovi)
                        ? "-bsf:v av1_metadata=remove_dovi=1"
                        : "-bsf:v dovi_rpu=strip=1",
                DynamicHdrMetadataRemoval.RemoveHdr10Plus => "-bsf:v av1_metadata=remove_hdr10plus=1",
                _ => null
            };
        }

        return null;
    }
}
