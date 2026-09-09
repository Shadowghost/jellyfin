using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace Emby.Server.Implementations.Hero;

/// <summary>
/// A configuration factory for <see cref="HeroOptions"/>.
/// </summary>
public class HeroConfigurationFactory : IConfigurationFactory
{
    /// <inheritdoc />
    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        return new[]
        {
            new ConfigurationStore
            {
                ConfigurationType = typeof(HeroOptions),
                Key = "hero"
            }
        };
    }
}
