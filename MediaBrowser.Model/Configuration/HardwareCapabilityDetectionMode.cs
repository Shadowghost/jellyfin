namespace MediaBrowser.Model.Configuration;

/// <summary>
/// Enum containing hardware capability detection modes.
/// </summary>
public enum HardwareCapabilityDetectionMode
{
    /// <summary>
    /// Use the ffprobe capability report when available and probe the encoder otherwise.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always probe the encoder, even when ffprobe can report the capabilities directly.
    /// </summary>
    Probe = 1,

    /// <summary>
    /// Never detect hardware capabilities.
    /// </summary>
    Disabled = 2
}
