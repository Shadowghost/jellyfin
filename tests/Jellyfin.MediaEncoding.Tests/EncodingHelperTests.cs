using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.MediaEncoding.Tests
{
    public class EncodingHelperTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData(Video3DFormat.MVC, "")]
        [InlineData(Video3DFormat.FullSideBySide, "crop=trunc(iw/4)*2:ih:0:0")]
        [InlineData(Video3DFormat.HalfSideBySide, "crop=trunc(iw/4)*2:ih:0:0,scale=iw*2:ih,setsar=sar=1")]
        [InlineData(Video3DFormat.FullTopAndBottom, "crop=iw:trunc(ih/4)*2:0:0")]
        [InlineData(Video3DFormat.HalfTopAndBottom, "crop=iw:trunc(ih/4)*2:0:0,scale=iw:ih*2,setsar=sar=1")]
        public void GetVideo3DFilter_Valid_Success(Video3DFormat? threedFormat, string expected)
        {
            Assert.Equal(expected, EncodingHelper.GetVideo3DFilter(threedFormat));
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
        [InlineData(null, "")]
        [InlineData(Video3DFormat.MVC, "")]
        [InlineData(Video3DFormat.FullSideBySide, "crop=trunc(iw/4)*2:ih:0:0")]
        [InlineData(Video3DFormat.HalfSideBySide, "crop=trunc(iw/4)*2:ih:0:0,setsar=sar=1")]
        [InlineData(Video3DFormat.FullTopAndBottom, "crop=iw:trunc(ih/4)*2:0:0")]
        [InlineData(Video3DFormat.HalfTopAndBottom, "crop=iw:trunc(ih/4)*2:0:0,setsar=sar=1")]
        public void GetVideo3DCropFilter_NeverAddsASoftwareScale(Video3DFormat? threedFormat, string expected)
        {
            // The hardware scaler downstream stretches the half formats back, a software scale would copy back.
            Assert.Equal(expected, EncodingHelper.GetVideo3DCropFilter(threedFormat));
        }
    }
}
