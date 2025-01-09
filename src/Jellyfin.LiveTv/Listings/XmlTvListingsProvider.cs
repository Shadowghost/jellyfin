#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using Jellyfin.XmlTv;
using Jellyfin.XmlTv.Entities;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.LiveTv.Listings;

/// <summary>
/// XMLTV Listing provider.
/// </summary>
public class XmlTvListingsProvider : IListingsProvider
{
    private static readonly TimeSpan _maxCacheAge = TimeSpan.FromHours(1);

    private readonly IServerConfigurationManager _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<XmlTvListingsProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlTvListingsProvider" /> class.
    /// </summary>
    /// <param name="config">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{XmlTvListingsProvider}"/> interface.</param>
    public XmlTvListingsProvider(
        IServerConfigurationManager config,
        IHttpClientFactory httpClientFactory,
        ILogger<XmlTvListingsProvider> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "XmlTV";

    /// <inheritdoc/>
    public string Type => "xmltv";

    private string GetLanguage(ListingsProviderInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.PreferredLanguage))
        {
            return info.PreferredLanguage;
        }

        return _config.Configuration.PreferredMetadataLanguage;
    }

    private async Task<string> GetXml(ListingsProviderInfo info, CancellationToken cancellationToken)
    {
        _logger.LogInformation("xmltv path: {Path}", info.Path);

        string cacheFilename = info.Id + ".xml";
        string cacheFile = Path.Combine(_config.ApplicationPaths.CachePath, "xmltv", cacheFilename);

        if (File.Exists(cacheFile) && File.GetLastWriteTimeUtc(cacheFile) >= DateTime.UtcNow.Subtract(_maxCacheAge))
        {
            return cacheFile;
        }

        // Must check if file exists as parent directory may not exist.
        if (File.Exists(cacheFile))
        {
            File.Delete(cacheFile);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
        }

        if (info.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Downloading xmltv listings from {Path}", info.Path);

            using var response = await _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(info.Path, cancellationToken).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await UnzipIfNeededAndCopy(info.Path, stream, cacheFile, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            var stream = AsyncFile.OpenRead(info.Path);
            await using (stream.ConfigureAwait(false))
            {
                return await UnzipIfNeededAndCopy(info.Path, stream, cacheFile, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> UnzipIfNeededAndCopy(string originalUrl, Stream stream, string file, CancellationToken cancellationToken)
    {
        var fileStream = new FileStream(
            file,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            IODefaults.FileStreamBufferSize,
            FileOptions.Asynchronous);

        await using (fileStream.ConfigureAwait(false))
        {
            if (Path.GetExtension(originalUrl.AsSpan().LeftPart('?')).Equals(".gz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var reader = new GZipStream(stream, CompressionMode.Decompress);
                    await reader.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting from gz file {File}", originalUrl);
                }
            }
            else
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return file;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentNullException(nameof(channelId));
        }

        _logger.LogDebug("Getting xmltv programs for channel {Id}", channelId);

        string path = await GetXml(info, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Opening XmlTvReader for {Path}", path);
        var reader = new XmlTvReader(path, GetLanguage(info));

        return reader.GetProgrammes(channelId, startDateUtc, endDateUtc, cancellationToken)
                    .Select(p => GetProgramInfo(p, info));
    }

    private static ProgramInfo GetProgramInfo(XmlTvProgram program, ListingsProviderInfo info)
    {
        string episodeTitle = program.Episode.Title;
        var programCategories = program.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        var rating = program.Ratings?.FirstOrDefault()?.Value.NullIfEmpty();
        var backdrop = program.Images.FirstOrDefault(i => i.Type == XmlTv.Enums.ImageType.Backdrop)?.Path;
        var poster = program.Images.FirstOrDefault(i => i.Type == XmlTv.Enums.ImageType.Poster)?.Path;
        var icon = program.Icons.OrderBy(i => i.Width * i.Height).FirstOrDefault()?.Source;
        var image = backdrop.NullIfEmpty() ?? poster.NullIfEmpty() ?? icon.NullIfEmpty();

        var programInfo = new ProgramInfo
        {
            ChannelId = program.ChannelId,
            EndDate = program.EndDate.UtcDateTime,
            EpisodeNumber = program.Episode.Episode,
            EpisodeTitle = episodeTitle,
            Genres = programCategories,
            StartDate = program.StartDate.UtcDateTime,
            Name = program.Title,
            Overview = program.Description,
            ProductionYear = program.CopyrightDate?.Year,
            SeasonNumber = program.Episode.Series,
            IsSeries = program.Episode.Episode is not null,
            IsRepeat = program.IsPreviouslyShown && !program.IsNew,
            IsPremiere = program.Premiere is not null,
            IsKids = programCategories.Any(c => info.KidsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
            IsMovie = programCategories.Any(c => info.MovieCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
            IsNews = programCategories.Any(c => info.NewsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
            IsSports = programCategories.Any(c => info.SportsCategories.Contains(c, StringComparison.OrdinalIgnoreCase)),
            ImageUrl = image,
            HasImage = image is not null,
            OfficialRating = rating,
            CommunityRating = (float?)program.StarRatings.FirstOrDefault().StarRating,
            SeriesId = program.Episode.Episode is null ? null : program.Title?.GetMD5().ToString("N", CultureInfo.InvariantCulture)
        };

        if (string.IsNullOrWhiteSpace(program.ProgramId))
        {
            string uniqueString = (program.Title ?? string.Empty) + (episodeTitle ?? string.Empty);

            if (programInfo.SeasonNumber.HasValue)
            {
                uniqueString = "-" + programInfo.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (programInfo.EpisodeNumber.HasValue)
            {
                uniqueString = "-" + programInfo.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            programInfo.ShowId = uniqueString.GetMD5().ToString("N", CultureInfo.InvariantCulture);

            // If we don't have valid episode info, assume it's a unique program, otherwise recordings might be skipped
            if (programInfo.IsSeries
                && !programInfo.IsRepeat
                && (programInfo.EpisodeNumber ?? 0) == 0)
            {
                programInfo.ShowId += programInfo.StartDate.Ticks.ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            programInfo.ShowId = program.ProgramId;
        }

        // Construct an id from the channel and start date
        programInfo.Id = string.Format(CultureInfo.InvariantCulture, "{0}_{1:O}", program.ChannelId, program.StartDate);

        if (programInfo.IsMovie)
        {
            programInfo.IsSeries = false;
            programInfo.EpisodeNumber = null;
            programInfo.EpisodeTitle = null;
        }

        return programInfo;
    }

    /// <inheritdoc/>
    public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
    {
        // Assume all urls are valid. check files for existence
        if (!info.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !File.Exists(info.Path))
        {
            throw new FileNotFoundException("Could not find the XmlTv file specified:", info.Path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
    {
        // In theory this should never be called because there is always only one lineup
        string path = await GetXml(info, CancellationToken.None).ConfigureAwait(false);
        _logger.LogDebug("Opening XmlTvReader for {Path}", path);
        var reader = new XmlTvReader(path, GetLanguage(info));
        IEnumerable<XmlTvChannel> results = reader.GetChannels();

        // Should this method be async?
        return results.Select(c => new NameIdPair() { Id = c.Id, Name = c.DisplayName }).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
    {
        // In theory this should never be called because there is always only one lineup
        string path = await GetXml(info, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Opening XmlTvReader for {Path}", path);
        var reader = new XmlTvReader(path, GetLanguage(info));
        var results = reader.GetChannels();

        // Should this method be async?
        return results.Select(c => new ChannelInfo
        {
            Id = c.Id,
            Name = c.DisplayName,
            ImageUrl = c.Icons.FirstOrDefault()?.Source.NullIfEmpty(),
            Number = string.IsNullOrWhiteSpace(c.Number) ? c.Id : c.Number
        }).ToList();
    }
}
