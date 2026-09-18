namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// Extensions for <see cref="FrameSurface"/>.
/// </summary>
public static class FrameSurfaceExtensions
{
    /// <summary>
    /// Gets the name ffmpeg knows the device by, as used by <c>hwmap=derive_device</c>.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The device name, or <c>null</c> for system memory.</returns>
    public static string? GetDeviceName(this FrameSurface surface) => surface switch
    {
        FrameSurface.Cuda => "cuda",
        FrameSurface.Qsv => "qsv",
        FrameSurface.Vaapi => "vaapi",
        FrameSurface.Vulkan => "vulkan",
        FrameSurface.OpenCl => "opencl",
        FrameSurface.VideoToolbox => "videotoolbox",
        FrameSurface.Rkmpp => "rkmpp",
        FrameSurface.D3d11 => "d3d11va",
        _ => null
    };

    /// <summary>
    /// Gets the frame type a surface holds, which a map onto it has to be followed by.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The format name, or <c>null</c> when a map needs no format behind it.</returns>
    public static string? GetMappedFormatName(this FrameSurface surface) => surface switch
    {
        FrameSurface.Qsv => "qsv",
        FrameSurface.Rkmpp => "drm_prime",
        FrameSurface.D3d11 => "d3d11",
        FrameSurface.Vaapi => "vaapi",
        _ => null
    };

    /// <summary>
    /// Gets the filter that uploads a frame from system memory onto the surface.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The filter name, or <c>null</c> when the surface cannot be uploaded to.</returns>
    public static string? GetUploadFilterName(this FrameSurface surface) => surface switch
    {
        FrameSurface.Cuda => "hwupload_cuda",
        FrameSurface.Vaapi => "hwupload_vaapi",
        FrameSurface.System => null,
        _ => "hwupload"
    };
}
