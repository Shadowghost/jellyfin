namespace Jellyfin.MediaEncoding.Pipeline.Input;

/// <summary>
/// The names the command line refers to each initialised hardware device by.
/// </summary>
public static class HardwareDeviceAliases
{
    /// <summary>
    /// Intel Quick Sync Video.
    /// </summary>
    public const string Qsv = "qs";

    /// <summary>
    /// VA-API.
    /// </summary>
    public const string Vaapi = "va";

    /// <summary>
    /// Direct3D 11.
    /// </summary>
    public const string D3d11va = "dx11";

    /// <summary>
    /// VideoToolbox.
    /// </summary>
    public const string VideoToolbox = "vt";

    /// <summary>
    /// Rockchip MPP.
    /// </summary>
    public const string Rkmpp = "rk";

    /// <summary>
    /// OpenCL.
    /// </summary>
    public const string OpenCl = "ocl";

    /// <summary>
    /// NVIDIA CUDA.
    /// </summary>
    public const string Cuda = "cu";

    /// <summary>
    /// Direct Rendering Manager.
    /// </summary>
    public const string Drm = "dr";

    /// <summary>
    /// Vulkan.
    /// </summary>
    public const string Vulkan = "vk";
}
