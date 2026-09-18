namespace Jellyfin.MediaEncoding.Pipeline.BitStreams;

/// <summary>
/// A bitstream filter option the installed ffmpeg may or may not have been built with.
/// </summary>
public enum BitStreamFilterOption
{
    /// <summary>
    /// <c>hevc_metadata=remove_dovi</c>.
    /// </summary>
    HevcMetadataRemoveDovi = 0,

    /// <summary>
    /// <c>hevc_metadata=remove_hdr10plus</c>.
    /// </summary>
    HevcMetadataRemoveHdr10Plus = 1,

    /// <summary>
    /// <c>av1_metadata=remove_dovi</c>.
    /// </summary>
    Av1MetadataRemoveDovi = 2,

    /// <summary>
    /// <c>av1_metadata=remove_hdr10plus</c>.
    /// </summary>
    Av1MetadataRemoveHdr10Plus = 3,

    /// <summary>
    /// <c>dovi_rpu=strip</c>.
    /// </summary>
    DoviRpuStrip = 4
}
