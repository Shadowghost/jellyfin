namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// Which of the legacy vendor chains a case is measured against.
/// </summary>
/// <remarks>
/// <see cref="Auto"/> goes through the dispatcher, which picks the chain from the operating system
/// and the detected driver. The rest name a chain directly, so a chain that this machine could never
/// reach can still be compared.
/// </remarks>
internal enum ChainVariant
{
    /// <summary>
    /// Whatever the dispatcher picks here.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Apple VideoToolbox.
    /// </summary>
    VideoToolbox = 1,

    /// <summary>
    /// AMD AMF on Direct3D 11.
    /// </summary>
    AmdD3d11 = 2,

    /// <summary>
    /// Intel Quick Sync Video on Direct3D 11.
    /// </summary>
    QsvD3d11 = 3,

    /// <summary>
    /// VA-API on the Intel iHD driver, which has its own video processor.
    /// </summary>
    VaapiIntelFull = 4,

    /// <summary>
    /// VA-API on AMD with Vulkan, which tone maps with libplacebo.
    /// </summary>
    VaapiAmdFull = 5
}
