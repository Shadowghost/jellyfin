namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// The memory a video frame lives in while a filter chain runs.
/// </summary>
public enum FrameSurface
{
    /// <summary>
    /// System memory.
    /// </summary>
    System = 0,

    /// <summary>
    /// NVIDIA CUDA device memory.
    /// </summary>
    Cuda = 1,

    /// <summary>
    /// Intel Quick Sync Video device memory.
    /// </summary>
    Qsv = 2,

    /// <summary>
    /// VA-API device memory.
    /// </summary>
    Vaapi = 3,

    /// <summary>
    /// Vulkan device memory.
    /// </summary>
    Vulkan = 4,

    /// <summary>
    /// OpenCL device memory.
    /// </summary>
    OpenCl = 5,

    /// <summary>
    /// VideoToolbox device memory.
    /// </summary>
    VideoToolbox = 6,

    /// <summary>
    /// Rockchip MPP device memory.
    /// </summary>
    Rkmpp = 7,

    /// <summary>
    /// Direct3D 11 device memory.
    /// </summary>
    D3d11 = 8
}
