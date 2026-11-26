#nullable enable

using System;

namespace Barotrauma.Plugins;

/// <summary>
/// The main entry point for a Barotrauma plugin.
/// The interface implements <see cref="IDisposable"/> to allow for cleanup when the plugin is unloaded.
/// </summary>
/// <example>
/// <code>
/// internal sealed class MyBarotraumaPlugin : IBarotraumaPlugin
/// {
///     public static IDebugConsole DebugConsole = PluginServiceProvider.GetService&lt;IDebugConsole&gt;();
/// 
///     public void Init()
///     {
///         DebugConsole.NewMessage("My plugin was enabled!");
///         // Call services here.
///     }
///
///     public void Dispose()
///     {
///         DebugConsole.NewMessage("My plugin was disabled!");
///         // The plugin was disabled, clean up resources if needed.
///     }
/// }
/// </code>
/// </example>
public interface IBarotraumaPlugin : IDisposable
{
    /// <summary>
    /// Used to initialize the plugin.
    /// This is where you would call plugin services to register events, hooks and whatever else you need.
    /// </summary>
    void Init();

    /// <summary>
    /// Called after all the content of the content package has been loaded.
    /// </summary>
    void OnContentLoaded();
}