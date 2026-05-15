using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Providers.Plugins.GoogleBooks;

/// <summary>
/// Google Books plugin instance.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasEmbeddedImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override Guid Id => new("9f97232d-e7f4-432d-92d3-c709ce47e30b");

    /// <inheritdoc />
    public override string Name => "Google Books";

    /// <inheritdoc />
    public override string Description => "Get external links for books from Google Books.";

    /// <inheritdoc />
    // TODO remove when plugin removed from server.
    public override string ConfigurationFileName => "Jellyfin.Plugin.GoogleBooks.xml";

    /// <inheritdoc />
    public string ImageResourceName => GetType().Namespace + ".jellyfin-plugin-googlebooks.svg";
}
