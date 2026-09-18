namespace Jellyfin.MediaEncoding.Pipeline.Output;

/// <summary>
/// What a job is allowed to encode with.
/// </summary>
/// <param name="HardwareEncoding">Whether hardware encoding is turned on at all.</param>
/// <param name="HardwareDisabled">Whether this particular job has been pushed back to software.</param>
/// <param name="BitDepth">The bits per colour component the output carries.</param>
public readonly record struct VideoEncoderRequest(bool HardwareEncoding, bool HardwareDisabled, int BitDepth);
