namespace MediaBrowser.Controller.MediaEncoding;

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
