using System;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Allows plugins to register callback functions that get called at specific points in the game.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly ISimpleHookService HookService = PluginServiceProvider.GetService&lt;ISimpleHookService&gt;();
    /// </code>
    /// </remarks>
    public interface ISimpleHookService : IPluginService
    {
        /// <summary>
        /// Registers a hook that gets called at a specific point in the game.
        /// </summary>
        /// <param name="callback">A method reference, Action, Func or delegate to the hook you want to register.</param>
        /// <typeparam name="T">The delegate type of the hook you want to register. </typeparam>
        /// <remarks>
        /// Make sure to specify the correct delegate type for the hook you want to register.
        /// A list of available delegate types can be found in SharedSource\Plugins\Services\HookDelegates.cs.
        /// </remarks>
        /// <example>
        /// To register a hook, you need to specify the delegate type and the method you want to call.
        /// <code>
        /// HookService.RegisterHook&lt;PluginOnPreUpdateDelegate&gt;(MyOnPreUpdate);
        /// 
        /// public static void MyOnPreUpdate(float deltaTime) { /* Do something */ }
        /// </code>
        /// You may also use a lambda expression to register a hook:
        /// <code>
        /// HookService.RegisterHook&lt;PluginOnPreUpdateDelegate&gt;(deltaTime => { /* Do something */ });
        /// </code>
        /// Using a lambda this way doesn't  allow you to manually deregister the hook on your own using <see cref="DeregisterHook{T}"/>
        /// unless you store a reference to the lambda in a variable, but at that point you might as well use a method.
        /// Regardless the game will still automatically deregister everything when your plugin is unloaded.
        /// </example>
        public void RegisterHook<T>(T callback) where T : Delegate;

        /// <summary>
        /// Deregister a hook that was previously registered using <see cref="RegisterHook{T}"/>.
        /// </summary>
        /// <param name="callback">A method reference to the hook you want to deregister.</param>
        /// <typeparam name="T">Delegate type of the hook you want to deregister.</typeparam>
        /// <remarks>
        /// You do not need to manually deregister hooks when your plugin is unloaded, this is done automatically.
        /// </remarks>
        /// <example>
        /// To deregister a hook:
        /// <code>
        /// HookService.DeregisterHook(MyOnPreUpdate);
        /// </code>
        /// You may omit the generic type as the reference to the callback is used to find and deregister it.
        /// </example>
        public void DeregisterHook<T>(T callback) where T : Delegate;
    }
}