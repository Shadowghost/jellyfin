namespace MediaBrowser.Providers.Plugins.Tmdb
{
    /// <summary>
    /// The name a TMDb genre carries in some language, and the id behind it.
    /// </summary>
    /// <param name="Name">The name.</param>
    /// <param name="Id">The TMDb genre id.</param>
    public readonly record struct TmdbGenreName(string Name, int Id);
}
