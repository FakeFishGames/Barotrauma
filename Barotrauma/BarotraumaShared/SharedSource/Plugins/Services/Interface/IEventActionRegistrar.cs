using System;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Provides methods for registering custom event actions to be loaded in <see cref="ScriptedEvent"/>.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IEventActionRegistrar EventActionRegistrar = PluginServiceProvider.GetService&lt;IEventActionRegistrar&gt;();
    /// </code>
    /// </remarks>
    public interface IEventActionRegistrar : IPluginService
    {
        /// <summary>
        /// Registers a custom event action to be recognized by <see cref="ScriptedEvent"/>.
        /// </summary>
        /// <typeparam name="T">Implementation of <see cref="EventAction"/> to register.</typeparam>
        /// <example>
        /// MyEventAction.cs:
        /// <code>
        /// public class MyEventAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
        /// {
        ///     private bool isFinished;
        ///     
        ///     public override bool IsFinished(ref string goToLabel)
        ///         => isFinished;
        ///     
        ///     public override void Reset()
        ///         => isFinished = false;
        ///     
        ///     public override void Update(float deltaTime)
        ///     {
        ///         // Do something
        ///         isFinished = true;
        ///     }
        /// }
        /// </code>
        /// Plugin init:
        /// <code>
        /// EventActionRegistrar.RegisterEventAction&lt;MyEventAction&gt;();
        /// </code>
        /// </example>
        public void RegisterEventAction<T>() where T : EventAction;

        /// <summary>
        /// Registers multiple custom event actions to be recognized by <see cref="ScriptedEvent"/>.
        /// </summary>
        /// <param name="types">Types of <see cref="EventAction"/> to register.</param>
        /// <example>
        /// MyEventAction.cs:
        /// <code>
        /// public class MyEventAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
        /// {
        ///     private bool isFinished;
        ///     
        ///     public override bool IsFinished(ref string goToLabel)
        ///         => isFinished;
        ///     
        ///     public override void Reset()
        ///         => isFinished = false;
        ///     
        ///     public override void Update(float deltaTime)
        ///     {
        ///         // Do something
        ///         isFinished = true;
        ///     }
        /// }
        /// </code>
        /// Plugin init:
        /// <code>
        /// EventActionRegistrar.RegisterEventActions(typeof(MyEventAction), typeof(MyOtherEventAction));
        /// </code>
        /// </example>
        /// <exception cref="ArgumentException">if a type does not inherit <see cref="EventAction"/>.</exception>
        public void RegisterEventActions(params Type[] types);

        /// <summary>
        /// Registers all types in the calling plugin that inherit <see cref="EventAction"/>.
        /// </summary>
        /// <remarks>
        /// The method will find all types in the calling assembly that inherit <see cref="EventAction"/>
        /// and register them using <see cref="RegisterEventActions"/>.
        /// </remarks>
        /// <example>
        /// Plugin init:
        /// <code>
        /// EventActionRegistrar.RegisterAllEventActions();
        /// </code>
        /// </example>
        public void RegisterAllEventActions();
    }
}