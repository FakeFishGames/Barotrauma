#nullable enable

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Provides a way for plugins to get their own Content Package and check for dependencies.
    /// </summary>
    /// <remarks>
    /// The members are instance members. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IContentPackageRetrievalService ContentPackageRetrieval = PluginServiceProvider.GetService&lt;IContentPackageRetrievalService&gt;();
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// &lt;ContentPackage name="foobar"&gt;
    ///   &lt;Dependency identifier="examplemod" Names="ExampleMod" /&gt;
    /// &lt;/ContentPackage&gt;
    /// </code>
    /// <code>
    /// ContentPackage contentPackage = ContentPackageRetrieval.ContentPackage;
    /// </code>
    /// <code>
    /// if (ContentPackageRetrieval.IsDependencyPresent("examplemod"))
    /// {
    ///     // Do something
    /// }
    /// </code>
    /// </example>
    public interface IContentPackageRetrievalService : IPluginService
    {
        /// <summary>
        /// Content Package that loaded your Plugin Assembly.
        /// </summary>
        public ContentPackage ContentPackage { get; }

        /// <summary>
        /// Checks if a content package dependency is present using the dependency identifier.
        /// </summary>
        public bool IsDependencyPresent(Identifier dependencyIdentifier);
    }
}