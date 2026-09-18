namespace Jellyfin.MediaEncoding.Pipeline.BitStreams;

/// <summary>
/// Which dynamic HDR metadata a stream has to be stripped of before a client will play it.
/// </summary>
public enum DynamicHdrMetadataRemoval
{
    /// <summary>
    /// The stream is handed on untouched.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Dolby Vision RPU is removed, leaving the base layer.
    /// </summary>
    RemoveDovi = 1,

    /// <summary>
    /// The HDR10+ metadata is removed, leaving plain HDR10.
    /// </summary>
    RemoveHdr10Plus = 2
}
