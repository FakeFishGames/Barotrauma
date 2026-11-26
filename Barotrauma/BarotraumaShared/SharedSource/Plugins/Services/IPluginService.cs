#nullable enable

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Empty interface to avoid using the object type for plugin services.<br/>
    /// Every plugin service should implement this in the interface declaration
    /// so they can be gathered into a dictionary in <see cref="PluginServiceProvider"/>.
    /// </summary>
    /// <example>
    /// Interface declaration:
    /// <code>
    /// public interface IExampleService : IPluginService { }
    /// public interface IExampleTransientService : IPluginService { }
    /// </code>
    /// Simple singleton implementation:
    /// <code>
    /// internal sealed class ImplExampleService : IExampleService { }
    /// </code>
    /// Transient implementation, implement <see cref="ITransientService"/> along with our service interface:
    /// <code>
    /// internal sealed class ImplExampleTransientService : ITransientService, IExampleTransientService
    /// {
    ///     public ImplExampleTransientService(PluginData data, Assembly assembly)
    ///         => PluginLoader.AddTransientService(assembly, this);
    ///
    ///     public void Dispose() { /* Dispose resources */ }
    /// }
    /// </code>
    /// PluginServiceProvider.cs:
    /// <code>
    ///   private static readonly PluginServiceProvider Services =
    ///     new PluginServiceBuilder()
    ///         .AddSingleton&lt;IExampleService, ImplExampleService&gt;()
    ///         .AddTransient&lt;IExampleTransientService&gt;(CreateTransient&lt;ImplExampleTransientService&gt;)
    ///         .Build();
    /// </code>
    /// </example>
    public interface IPluginService;
}