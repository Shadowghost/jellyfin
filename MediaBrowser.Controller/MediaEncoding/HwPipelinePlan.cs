using System;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.MediaEncoding;

/// <summary>
/// The per pixel operations a filter chain has to perform.
/// </summary>
[Flags]
public enum HwOps
{
    /// <summary>
    /// No operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Scaling and pixel format conversion.
    /// </summary>
    Scale = 1,

    /// <summary>
    /// Cropping, either to flatten frame packed 3D or to honour cropping metadata.
    /// </summary>
    Crop = 2,

    /// <summary>
    /// Deinterlacing.
    /// </summary>
    Deinterlace = 4,

    /// <summary>
    /// HDR to SDR tone mapping.
    /// </summary>
    Tonemap = 8,

    /// <summary>
    /// Burning subtitles into the picture.
    /// </summary>
    SubtitleOverlay = 16,

    /// <summary>
    /// Rotating the picture.
    /// </summary>
    Transpose = 32
}

/// <summary>
/// The device a filter chain runs its operations on.
/// </summary>
public enum HwFilterDevice
{
    /// <summary>
    /// Software filtering.
    /// </summary>
    None = 0,

    /// <summary>
    /// Intel Quick Sync Video, filtering with vpp_qsv.
    /// </summary>
    Qsv = 1,

    /// <summary>
    /// VA-API, filtering with scale_vaapi and friends.
    /// </summary>
    Vaapi = 2,

    /// <summary>
    /// NVIDIA CUDA, filtering with scale_cuda and friends.
    /// </summary>
    Cuda = 3,

    /// <summary>
    /// Vulkan, filtering with libplacebo.
    /// </summary>
    Vulkan = 4,

    /// <summary>
    /// OpenCL, filtering with scale_opencl and friends.
    /// </summary>
    OpenCl = 5,

    /// <summary>
    /// VideoToolbox, filtering with scale_vt and friends.
    /// </summary>
    VideoToolbox = 6,

    /// <summary>
    /// Rockchip RGA, filtering with vpp_rkrga.
    /// </summary>
    Rkrga = 7
}

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
