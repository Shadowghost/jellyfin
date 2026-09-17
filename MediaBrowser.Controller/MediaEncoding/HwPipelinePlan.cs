using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// What a transcoding job intends to do in hardware, decided once before the filter chain is built.
/// </summary>
/// <param name="AccelerationType">The configured hardware acceleration type.</param>
/// <param name="FilterDevice">The device the filter chain runs on.</param>
/// <param name="RequiredOps">The operations the chain has to perform.</param>
/// <param name="CanCropInVram">Whether the filter device can crop without a round trip through system memory.</param>
public readonly record struct HwPipelinePlan(
    HardwareAccelerationType AccelerationType,
    HwFilterDevice FilterDevice,
    HwOps RequiredOps,
    bool CanCropInVram)
{
    /// <summary>
    /// Gets a value indicating whether the chain has to leave the device to crop.
    /// </summary>
    public bool RequiresSoftwareCrop => RequiredOps.HasFlag(HwOps.Crop) && !CanCropInVram;
}
