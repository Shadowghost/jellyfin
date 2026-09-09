using System;
using System.Collections.Generic;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Hero;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// The hero section controller.
/// </summary>
[Route("")]
[Authorize]
public class HeroController : BaseJellyfinApiController
{
    private readonly IHeroService _heroService;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeroController"/> class.
    /// </summary>
    /// <param name="heroService">Instance of the <see cref="IHeroService"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    public HeroController(IHeroService heroService, IUserManager userManager)
    {
        _heroService = heroService;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets the items to show in the hero section.
    /// </summary>
    /// <param name="userId">Optional. The user id, defaulting to the authenticated user.</param>
    /// <param name="parentId">Optional. Restrict the items to a library or folder.</param>
    /// <param name="limit">Optional. The number of items to return, capped by the server configuration.</param>
    /// <response code="200">Hero items returned.</response>
    /// <response code="404">User not found.</response>
    /// <returns>
    /// The hero items, which is an empty list when the hero section is disabled or nothing in the
    /// user's libraries qualifies.
    /// </returns>
    [HttpGet("Items/Hero")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<HeroItemDto>> GetHeroItems(
        [FromQuery] Guid? userId,
        [FromQuery] Guid? parentId,
        [FromQuery] int? limit)
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(_heroService.GetHeroItems(user, parentId, limit));
    }
}
