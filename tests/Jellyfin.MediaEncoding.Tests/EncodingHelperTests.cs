using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests
{
    public class EncodingHelperTests
    {
        [Theory]
        [InlineData(null, null, "")]
        [InlineData(Video3DFormat.MVC, null, "")]
        [InlineData(Video3DFormat.FullSideBySide, null, "crop=trunc(iw/4)*2:ih:0:0,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.FullSideBySide, "left_right", "crop=trunc(iw/4)*2:ih:0:0,sidedata=mode=delete:type=STEREO3D")]
        // The SAR of the codec describes the packed frame: the stretched view takes the frame's shape.
        [InlineData(Video3DFormat.HalfSideBySide, null, "crop=trunc(iw/4)*2:ih:0:0,setsar=sar*2:max=1000000,scale=iw*2:ih,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfSideBySide, "top_bottom", "crop=trunc(iw/4)*2:ih:0:0,setsar=sar*2:max=1000000,scale=iw*2:ih,sidedata=mode=delete:type=STEREO3D")]
        // The SAR of a Matroska StereoMode describes one view: the stretch keeps its shape.
        [InlineData(Video3DFormat.HalfSideBySide, "left_right", "crop=trunc(iw/4)*2:ih:0:0,scale=iw*2:ih,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfSideBySide, "right_left", "crop=trunc(iw/4)*2:ih:0:0,scale=iw*2:ih,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfSideBySide, "col_interleaved_lr", "crop=trunc(iw/4)*2:ih:0:0,scale=iw*2:ih,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.FullTopAndBottom, "top_bottom", "crop=iw:trunc(ih/4)*2:0:0,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfTopAndBottom, null, "crop=iw:trunc(ih/4)*2:0:0,setsar=sar/2:max=1000000,scale=iw:ih*2,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfTopAndBottom, "left_right", "crop=iw:trunc(ih/4)*2:0:0,setsar=sar/2:max=1000000,scale=iw:ih*2,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfTopAndBottom, "top_bottom", "crop=iw:trunc(ih/4)*2:0:0,scale=iw:ih*2,sidedata=mode=delete:type=STEREO3D")]
        [InlineData(Video3DFormat.HalfTopAndBottom, "row_interleaved_rl", "crop=iw:trunc(ih/4)*2:0:0,scale=iw:ih*2,sidedata=mode=delete:type=STEREO3D")]
        public void GetVideo3DFilter_Valid_Success(Video3DFormat? threedFormat, string? stereoMode, string expected)
        {
            Assert.Equal(expected, EncodingHelper.GetVideo3DFilter(threedFormat, new MediaStream { Type = MediaStreamType.Video, StereoMode = stereoMode }));
        }

        [Theory]
        [InlineData(null, 3840, 806, 3840, 806)]
        [InlineData(Video3DFormat.MVC, 3840, 806, 3840, 806)]
        [InlineData(Video3DFormat.FullSideBySide, 3840, 806, 1920, 806)]
        [InlineData(Video3DFormat.HalfSideBySide, 3840, 806, 3840, 806)]
        [InlineData(Video3DFormat.FullTopAndBottom, 3840, 806, 3840, 402)]
        [InlineData(Video3DFormat.HalfTopAndBottom, 3840, 806, 3840, 806)]
        public void GetVideo3DFlattenedSize_Valid_Success(Video3DFormat? threedFormat, int? width, int? height, int? expectedWidth, int? expectedHeight)
        {
            Assert.Equal((expectedWidth, expectedHeight), EncodingHelper.GetVideo3DFlattenedSize(threedFormat, width, height));
        }

        [Theory]
        [InlineData(Video3DFormat.FullSideBySide, 3840, 806, 1920, 806)]
        [InlineData(Video3DFormat.HalfSideBySide, 3840, 806, 1920, 806)]
        [InlineData(Video3DFormat.FullTopAndBottom, 3840, 806, 3840, 402)]
        [InlineData(Video3DFormat.HalfTopAndBottom, 3840, 806, 3840, 402)]
        public void GetVideo3DCropSize_Packed_ReturnsSingleView(Video3DFormat? threedFormat, int? width, int? height, int expectedWidth, int expectedHeight)
        {
            Assert.Equal((expectedWidth, expectedHeight), EncodingHelper.GetVideo3DCropSize(threedFormat, width, height));
        }

        [Theory]
        [InlineData(null, 3840, 806)]
        [InlineData(Video3DFormat.MVC, 3840, 806)]
        [InlineData(Video3DFormat.FullSideBySide, null, 806)]
        [InlineData(Video3DFormat.FullSideBySide, 3840, null)]
        public void GetVideo3DCropSize_NothingToCrop_ReturnsNull(Video3DFormat? threedFormat, int? width, int? height)
        {
            Assert.Null(EncodingHelper.GetVideo3DCropSize(threedFormat, width, height));
        }

        [Theory]
        [InlineData("scale", "vaapi", "nv12", "", "scale_vaapi=format=nv12")]
        [InlineData("vpp", "qsv", "nv12", "cw=1920:ch=806:cx=0:cy=0", "vpp_qsv=w=3840:h=806:cw=1920:ch=806:cx=0:cy=0:format=nv12")]
        [InlineData("vpp", "qsv", "", "cw=1920:ch=806:cx=0:cy=0", "vpp_qsv=w=3840:h=806:cw=1920:ch=806:cx=0:cy=0")]
        public void GetHwScaleFilter_CropArgs_ForcesExplicitSize(string prefix, string suffix, string format, string? cropArgs, string expected)
        {
            Assert.Equal(
                expected,
                EncodingHelper.GetHwScaleFilter(prefix, suffix, format, false, 3840, 806, null, null, null, null, cropArgs));
        }
    }
}
