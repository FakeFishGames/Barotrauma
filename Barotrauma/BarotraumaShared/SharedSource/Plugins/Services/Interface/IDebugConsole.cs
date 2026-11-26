#nullable enable
using System;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Plugins
{
    [Flags]
    public enum CommandFlags
    {
        None,

        /// <summary>
        /// Causes the command to only be executable if "enablecheats" command is used.
        /// </summary>
        IsCheat,

        /// <summary>
        /// Enabling this flag will cause the command to be executed on the client as-is in multiplayer
        /// instead of the default behavior of relaying the command to the server.
        /// </summary>
        /// <remarks>
        /// By default, when executing a command in multiplayer,
        /// the client sends the command to the server
        /// which then executes it and relays the response back to the client.
        /// </remarks>
        DoNotRelayToServer,
    }

    /// <summary>
    /// Provides an interface to <see cref="DebugConsole"/> for plugins.
    /// Allows registering commands and writing messages to F3 debug console.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IDebugConsole DebugConsole = PluginServiceProvider.GetService&lt;IDebugConsole&gt;();
    /// </code>
    /// </remarks>
    public interface IDebugConsole : IPluginService
    {
        /// <summary>
        /// Writes a standard message to the debug console.
        /// Use for general information messages or as responses to commands.
        /// </summary>
        /// <param name="message">
        /// Message to write,
        /// the message will be prefixed with the plugin and content package name.
        /// </param>
        /// <param name="color">
        /// Optional color to display the message in.
        /// If not provided, the message will be displayed in white.
        /// </param>
        /// <example>
        /// <c>[ContentPackageName][PluginName] This is a message.</c>
        /// </example>
        public void NewMessage(string message, Color? color = null);

        /// <summary>
        /// Writes an error message to the debug console.
        /// The message will always be displayed in red.
        /// </summary>
        /// <param name="message">
        /// Message to write;
        /// the message will be prefixed with the plugin and content package name.
        /// </param>
        /// <param name="exception">
        /// Optional exception to include in the message.
        /// If provided, the exception message and stack trace will be appended to the end of the message
        /// and the contents of the error can be copied to the clipboard by right-clicking the message.
        /// </param>
        public void ThrowError(string message, Exception? exception = null);

        /// <summary>
        /// Writes a warning message to the debug console.
        /// The message will always be displayed in yellow.
        /// </summary>
        /// <param name="message">
        /// Message to write;
        /// the message will be prefixed with "WARNING", the plugin name and content package name.
        /// </param>
        /// <example>
        /// <c>[ContentPackageName][PluginName] WARNING: This is a warning message</c>
        /// </example>
        public void AddWarning(string message);

        /// <summary>
        /// Delegate that is called when a command is executed.
        /// </summary>
        /// <param name="args">
        /// Array of arguments passed to the command.
        /// </param>
        /// <example>
        /// Executing <code>myCommand foo bar "baz qux"</code> will result in <c>args</c> being <c>["foo", "bar", "baz qux"]</c>.
        /// </example>
        public delegate void OnCommandExecutedDelegate(string[] args);

#if SERVER

        /// <summary>
        /// Registers a command with the debug console.
        /// </summary>
        /// <param name="command">
        /// The name of the command to register. Use '|' to register multiple aliases for the same command.
        /// </param>
        /// <param name="helpMessage">
        /// A brief description of what the command does that is shown when using the "help" command.
        /// </param>
        /// <param name="flags">
        /// <see cref="CommandFlags"/> that modify the behavior of the command.
        /// </param>
        /// <param name="onCommandExecuted">
        /// <see cref="OnCommandExecutedDelegate"/> that is called when the command is executed.
        /// </param>
        /// <param name="onCommandExecutedByClient">
        /// <see cref="OnCommandExecutedByClientDelegate"/> that is called when a client requests executing the command.
        /// </param>
        /// <param name="getValidArgs">
        /// If set, the function will be used to return vaild arguments (autocompletions) for the command.
        /// </param>
        /// <example>
        /// <code>
        /// DebugConsole.RegisterCommand(
        ///     command: "hello|helloworld",
        ///     helpMessage: "Prints 'Hello, World!' to the console.",
        ///     flags: CommandFlags.None,
        ///     onCommandExecuted: args => DebugConsole.NewMessage("Hello, World!"),        
        ///     onCommandExecutedByClient: (client, cursorPos, args) => { GameMain.Server.SendChatMessage($"Hello, {client.Name}!", ChatMessageType.Console); });
        /// </code>
        /// </example>
        public void RegisterCommand(string command, string helpMessage, CommandFlags flags, 
            OnCommandExecutedDelegate onCommandExecuted, OnCommandExecutedByClientDelegate? onCommandExecutedByClient = null, 
            Func<string[][]>? getValidArgs = null);

        /// <summary>
        /// Delegate that is called server-side when a client requests executing it.
        /// </summary>
        /// <param name="args">
        /// Array of arguments passed to the command.
        /// </param>
        /// <param name="client">
        /// The client executing the command.
        /// </param>
        public delegate void OnCommandExecutedByClientDelegate(Client client, Vector2 cursorPos, string[] args);
#else
        /// <summary>
        /// Registers a command with the debug console.
        /// </summary>
        /// <param name="command">
        /// The name of the command to register. Use '|' to register multiple aliases for the same command.
        /// </param>
        /// <param name="helpMessage">
        /// A brief description of what the command does that is shown when using the "help" command.
        /// </param>
        /// <param name="flags">
        /// <see cref="CommandFlags"/> that modify the behavior of the command.
        /// </param>
        /// <param name="onCommandExecuted">
        /// <see cref="OnCommandExecutedDelegate"/> that is called when the command is executed.
        /// </param>
        /// <param name="getValidArgs">
        /// If set, the function will be used to return vaild arguments (autocompletions) for the command.
        /// </param>
        /// <example>
        /// <code>
        /// DebugConsole.RegisterCommand(
        ///     command: "hello|helloworld",
        ///     helpMessage: "Prints 'Hello, World!' to the console.",
        ///     flags: CommandFlags.None,
        ///     onCommandExecuted: args => DebugConsole.NewMessage("Hello, World!"));
        /// </code>
        /// </example>
        public void RegisterCommand(string command, string helpMessage, CommandFlags flags, OnCommandExecutedDelegate onCommandExecuted,
            Func<string[][]>? getValidArgs = null);
#endif

        /// <summary>
        /// Starts the map testing coroutine used by the testmap/testmaps commands.
        /// Generates maps with validation, and optionally calls callbacks for each generated map.
        /// Useful for plugins that want to run additional analysis or simulations on generated maps.
        /// </summary>
        /// <param name="fixedSeed">If set, uses this seed instead of generating random ones.</param>
        /// <param name="amount">Number of maps to test. If null, runs until cancelled (by pressing C).</param>
        /// <param name="onMapGenerated">Called for each generated map.</param>
        /// <param name="onBatchFinished">Called once after all maps have been tested.</param>
        public void StartTestMaps(
            string? fixedSeed = null,
            int? amount = null,
            Action<string, Map, CampaignMode>? onMapGenerated = null,
            Action? onBatchFinished = null);

        /// <summary>
        /// Deregisters a command from the debug console.
        /// </summary>
        /// <param name="command">
        /// One of the aliases of the command to unregister or the '|' separated list of aliases
        /// that was used to register the command via <see cref="RegisterCommand"/>.
        /// </param>
        /// <remarks>
        /// Only the plugin that registered the command can unregister it, so you do not need
        /// to worry about accidentally unregistering a command from another plugin with the same name.
        /// </remarks>
        /// <example>
        /// <code>
        /// DebugConsole.DeregisterCommand("hello");
        /// DebugConsole.DeregisterCommand("helloworld");
        /// DebugConsole.DeregisterCommand("hello|helloworld");
        /// </code>
        /// </example>
        public void DeregisterCommand(string command);
    }

}