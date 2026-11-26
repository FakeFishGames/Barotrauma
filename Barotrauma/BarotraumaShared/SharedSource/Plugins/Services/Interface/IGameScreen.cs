using System;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Provides a service to add events that trigger on resolution change.
    /// </summary>
    public interface IGameScreen : IPluginService
    {
        /// <summary>
        /// Registers an action to be executed when the screen resolution is changed.
        /// </summary>
        /// <param name="identifier">
        /// Unique identifier of this resolution change event.
        /// If the identifier already exists, the old one will be replaced.
        /// </param>
        /// <param name="onResolutionChanged">
        /// An action to be executed when the screen resolution is changed.
        /// </param>
        /// <example>
        /// <code>
        /// public class MyInterface : IDisposable
        /// {
        ///     private static readonly Identifier eventIdentifier = nameof(MyInterface).ToIdentifier();
        ///     public MyInterface(RectTransform parent)
        ///     {
        ///         // build the UI...
        ///         GameScreen.RegisterResolutionChangeEvent(eventIdentifier, OnResolutionChanged);
        ///     }
        ///
        ///     private void OnResolutionChanged()
        ///     {
        ///         // reconstruct the UI...
        ///     }
        ///
        ///     public void Dispose()
        ///         => GameScreen.DeregisterResolutionChangeEvent(eventIdentifier);
        /// }
        /// </code>
        /// </example>
        public void RegisterResolutionChangeEvent(Identifier identifier, Action onResolutionChanged);

        /// <summary>
        /// Deregisters an event by identifier.
        /// </summary>
        /// <param name="identifier">Identifier of the event to be removed.</param>
        /// <remarks>
        /// The service provider will automatically clear all resolution change events
        /// when the plugin is unloaded. However, it is still good practice to deregister them manually
        /// to avoid the action from hindering garbage collection, for example, while in the main menu.
        /// </remarks>
        public void DeregisterResolutionChangeEvent(Identifier identifier);
    }
}