using MediaBrowser.Model.MediaEncoding.Hardware;

namespace MediaBrowser.MediaEncoding.Encoder;

/// <summary>
/// A cached hardware capability report, with what it has to match to still be valid.
/// </summary>
internal sealed class HwCapsCacheEntry
{
    /// <summary>
    /// Gets or sets the layout version of this entry.
    /// </summary>
    public int CacheVersion { get; set; }

    /// <summary>
    /// Gets or sets the ffmpeg version the report was collected with.
    /// </summary>
    public string? EncoderVersion { get; set; }

    /// <summary>
    /// Gets or sets the device the report was collected from.
    /// </summary>
    public string? DeviceKey { get; set; }

    /// <summary>
    /// Gets or sets the reported capabilities.
    /// </summary>
    public HwAccelCapabilities? Capabilities { get; set; }
}
