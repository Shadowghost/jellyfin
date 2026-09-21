namespace Jellyfin.MediaEncoding.Pipeline.Input;

/// <summary>
/// What the job needs from the devices before the filter chain is built.
/// </summary>
/// <param name="HardwareDecode">Whether the source is decoded on the device.</param>
/// <param name="NeedsOpenCl">Whether the chain borrows an OpenCL device, as a tone mapper does.</param>
public readonly record struct InputPlanRequest(bool HardwareDecode, bool NeedsOpenCl)
{
    /// <summary>
    /// Gets the render node the job runs on, as the capability report names it, or <c>null</c>
    /// when nothing was reported and the device has to be matched by vendor instead.
    /// </summary>
    public string? DevicePath { get; init; }

    /// <summary>
    /// Gets the index the capability report gives the device, or <c>null</c> when nothing was
    /// reported and the device has to be matched by vendor instead.
    /// </summary>
    public int? DeviceIndex { get; init; }

    /// <summary>
    /// Gets the rotation the source carries, in degrees.
    /// </summary>
    public int Rotation { get; init; }
}
