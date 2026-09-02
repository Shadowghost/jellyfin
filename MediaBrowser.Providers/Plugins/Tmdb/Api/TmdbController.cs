using System.Globalization;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Providers.Plugins.Tmdb.Lists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TMDbLib.Objects.General;

namespace MediaBrowser.Providers.Plugins.Tmdb.Api
{
    /// <summary>
    /// The TMDb API controller.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("[controller]")]
    [Produces(MediaTypeNames.Application.Json)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class TmdbController : ControllerBase
    {
        private readonly TmdbClientManager _tmdbClientManager;
        private readonly TmdbListSyncManager _tmdbListSyncManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbController"/> class.
        /// </summary>
        /// <param name="tmdbClientManager">The TMDb client manager.</param>
        /// <param name="tmdbListSyncManager">The TMDb list sync manager.</param>
        public TmdbController(TmdbClientManager tmdbClientManager, TmdbListSyncManager tmdbListSyncManager)
        {
            _tmdbClientManager = tmdbClientManager;
            _tmdbListSyncManager = tmdbListSyncManager;
        }

        /// <summary>
        /// Gets the TMDb image configuration options.
        /// </summary>
        /// <returns>The image portion of the TMDb client configuration.</returns>
        [HttpGet("ClientConfiguration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ConfigImageTypes?> TmdbClientConfiguration()
        {
            return (await _tmdbClientManager.GetClientConfiguration().ConfigureAwait(false)).Images;
        }

        /// <summary>
        /// Creates a local collection from a TMDb list, or re-syncs the contents of the collection
        /// previously created from that list.
        /// </summary>
        /// <param name="listId">The TMDb id of the list.</param>
        /// <response code="200">Collection created or re-synced.</response>
        /// <response code="400">The list id is not a TMDb list id.</response>
        /// <response code="404">TMDb has no public list with that id.</response>
        /// <returns>The result of the sync.</returns>
        [HttpPost("Lists/{listId}/Collection")]
        [Authorize(Policy = Policies.RequiresElevation)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TmdbListSyncResult>> SyncTmdbListCollection([FromRoute] string listId)
        {
            if (!int.TryParse(listId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedListId) || parsedListId <= 0)
            {
                return BadRequest("The TMDb list id must be a positive number.");
            }

            var result = await _tmdbListSyncManager
                .SyncListAsync(parsedListId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result is null)
            {
                return NotFound("TMDb has no public list with that id.");
            }

            return result;
        }
    }
}
