using System.ComponentModel.DataAnnotations;
using Jellyfin.Api.Attributes;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Serves artwork out of the locally cached jellyfin-artwork bundle.
/// </summary>
[Route("StudioImages")]
public class StudioImagesController : BaseJellyfinApiController
{
    private readonly IStudioArtworkResolver _artworkResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioImagesController"/> class.
    /// </summary>
    /// <param name="artworkResolver">Instance of the <see cref="IStudioArtworkResolver"/> interface.</param>
    public StudioImagesController(IStudioArtworkResolver artworkResolver)
    {
        _artworkResolver = artworkResolver;
    }

    /// <summary>
    /// Gets an image from the local artwork bundle.
    /// </summary>
    /// <param name="path">The path of the image below the bundle root, e.g. <c>studios/2/20th-television/thumb.webp</c>.</param>
    /// <response code="200">Image stream returned.</response>
    /// <response code="404">Image not found.</response>
    /// <returns>The image file, or a <see cref="NotFoundResult"/> when the bundle has no such file.</returns>
    [HttpGet("{**path}")]
    [HttpHead("{**path}", Name = "HeadStudioArtwork")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesImageFile]
    public ActionResult GetStudioArtwork([FromRoute, Required] string path)
    {
        if (!_artworkResolver.TryResolveArtworkFile(path, out var fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, MimeTypes.GetMimeType(fullPath));
    }
}
