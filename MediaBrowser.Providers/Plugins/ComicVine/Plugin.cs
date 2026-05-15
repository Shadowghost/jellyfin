using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Providers.Plugins.ComicVine;

/// <summary>
/// ComicVine plugin instance.
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
    public override Guid Id => new("3ade6fd1-c76c-4560-b2df-f6bca4c2332f");

    /// <inheritdoc />
    public override string Name => "Comic Vine";

    /// <inheritdoc />
    public override string Description => "Get external links for comic books from Comic Vine.";

    /// <inheritdoc />
    // TODO remove when plugin removed from server.
    public override string ConfigurationFileName => "Jellyfin.Plugin.ComicVine.xml";

    /// <inheritdoc />
    public string ImageResourceName => GetType().Namespace + ".jellyfin-plugin-comicvine.svg";
}
