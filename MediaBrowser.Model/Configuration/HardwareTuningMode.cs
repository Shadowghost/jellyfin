namespace MediaBrowser.Model.Configuration;

/// <summary>
/// Enum containing the ways the hardware decoding capability settings are decided.
/// </summary>
public enum HardwareTuningMode
{
    /// <summary>
    /// Answer the capability settings from the detected hardware, leaving the stored values untouched.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Take the capability settings from this configuration.
    /// </summary>
    Manual = 1
}
