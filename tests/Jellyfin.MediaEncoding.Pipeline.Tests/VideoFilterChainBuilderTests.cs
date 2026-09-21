using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Amf;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Graph;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Xunit;

namespace Jellyfin.MediaEncoding.Pipeline.Tests;

public class VideoFilterChainBuilderTests
{
    private static FrameState Interlaced1080p => new()
    {
        Surface = FrameSurface.System,
        PixelFormat = PixelFormat.YUV420P,
        Size = new FrameSize(1920, 1080),
        IsInterlaced = true
    };

    [Fact]
    public void Build_DropsTheDeviceScalerWhenTheFrameAlreadyHasTheSize()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);
        var input = Interlaced1080p with { IsInterlaced = false, Surface = FrameSurface.Qsv };

        var chain = builder.Build(
            input,
            [new ScaleFilter(new FrameSize(1920, 1080))],
            FrameSurface.Qsv,
            PixelFormat.NV12);

        // Only the output format pin survives; the resize itself is dropped.
        Assert.Equal("vpp_qsv=format=nv12", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_KeepsASoftwareScaleThatWasAskedForExplicitly()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(NoneAccelerator.Instance, capabilities);
        var input = Interlaced1080p with { IsInterlaced = false };

        var chain = builder.Build(
            input,
            [new ScaleFilter(new FrameSize(1920, 1080))],
            FrameSurface.System,
            PixelFormat.YUV420P);

        Assert.Equal("scale=trunc(1920/2)*2:trunc(1080/2)*2,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_FlattensFramePackedThreeDBeforeScaling()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(NoneAccelerator.Instance, capabilities);
        var input = Interlaced1080p with { IsInterlaced = false, Size = new FrameSize(3840, 1080) };

        var chain = builder.Build(
            input,
            [new Flatten3DFilter(Video3DFormat.FullSideBySide, false), new ScaleFilter(default(ScalingRequest))],
            FrameSurface.System,
            PixelFormat.YUV420P);

        Assert.Equal("crop=trunc(iw/4)*2:ih:0:0,format=yuv420p", chain.ToFilterArgument());
        Assert.Equal(new FrameSize(1920, 1080), chain.OutputState.Size);
    }

    [Fact]
    public void Build_UploadsBeforeTheFirstFilterThatNeedsTheDevice()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.Qsv,
            PixelFormat.NV12);

        Assert.Equal(
            "format=nv12,hwupload,deinterlace_qsv=mode=2,vpp_qsv=format=nv12",
            chain.ToFilterArgument());
        Assert.Equal(FrameSurface.Qsv, chain.OutputState.Surface);
    }

    [Fact]
    public void Build_FusesTheResizeAndTheFormatPinIntoOneFilter()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p with { IsInterlaced = false },
            [new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.Qsv,
            PixelFormat.NV12);

        // The resize and the output format pin are one pass over the frame, not two.
        Assert.Equal("format=nv12,hwupload,vpp_qsv=w=1280:h=720:format=nv12", chain.ToFilterArgument());
        Assert.Equal(new FrameSize(1280, 720), chain.OutputState.Size);
    }

    [Fact]
    public void Build_DownloadsWhenTheEncoderReadsFromSystemMemory()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.System,
            PixelFormat.NV12);

        // The device converts to the format the encoder reads before the frame comes home.
        Assert.Equal(
            "format=nv12,hwupload,deinterlace_qsv=mode=2,vpp_qsv=format=nv12,hwdownload,format=nv12",
            chain.ToFilterArgument());
        Assert.Equal(FrameSurface.System, chain.OutputState.Surface);
    }

    [Fact]
    public void Build_FallsBackToSoftwareWhenTheDeviceLacksTheFilter()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false), new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.System,
            PixelFormat.YUV420P);

        Assert.Equal("bwdif=0:-1:0,scale=trunc(1280/2)*2:trunc(720/2)*2,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_FallsBackToSoftwareWhenTheDeviceRejectsTheUploadFormat()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif")
        {
            SurfaceFormatsAccepted = false
        };
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.System,
            PixelFormat.YUV420P);

        Assert.Equal("bwdif=0:-1:0,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_KeepsTheOperationInSoftwareWhenTheDeviceCannotPerformIt()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif")
        {
            UnsupportedOperation = HwVppKind.Deinterlace
        };
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false), new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.Qsv,
            PixelFormat.NV12);

        Assert.Equal(
            "bwdif=0:-1:0,format=nv12,hwupload,vpp_qsv=w=1280:h=720:format=nv12",
            chain.ToFilterArgument());
    }

    [Fact]
    public void BuildGraph_DrawsTheSubtitleOnTheOpenClDeviceTheAmfChainFiltersOn()
    {
        var capabilities = new FakePipelineCapabilities("scale_opencl", "overlay_opencl");
        var builder = new VideoFilterChainBuilder(new AmfD3d11Accelerator(), capabilities);
        var input = Interlaced1080p with { IsInterlaced = false, Surface = FrameSurface.D3d11, PixelFormat = PixelFormat.NV12 };

        var graph = builder.BuildGraph(
            input,
            [new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.D3d11,
            PixelFormat.NV12,
            new SubtitleSource(false) { Size = new FrameSize(1920, 1080) });

        Assert.Equal(
            "scale,scale=1280:720:fast_bilinear,format=yuva420p,hwupload=derive_device=opencl",
            graph.Subtitle?.ToFilterArgument());
        Assert.Equal(
            "overlay_opencl=eof_action=pass:repeatlast=0",
            graph.Overlay?.ToFilterArgument());
    }

    [Fact]
    public void AmfTransfer_OnlyMapsHomeFromTheOpenClSurface()
    {
        var accelerator = new AmfD3d11Accelerator();
        var onOpenCl = Interlaced1080p with { Surface = FrameSurface.OpenCl, PixelFormat = PixelFormat.NV12 };
        var onD3d11 = onOpenCl with { Surface = FrameSurface.D3d11 };

        Assert.Equal(
            "hwmap=mode=read,format=nv12",
            accelerator.CreateTransferFilter(FrameSurface.System, onOpenCl, false)?.ToFilterArgument());

        // hwmap=mode=read has no Direct3D 11 side, so the builder is left to copy that frame home.
        Assert.Null(accelerator.CreateTransferFilter(FrameSurface.System, onD3d11, false));

        // Nor is a frame in system memory mapped onto a device it was never on.
        Assert.Null(accelerator.CreateTransferFilter(FrameSurface.OpenCl, Interlaced1080p, false));
    }
}
