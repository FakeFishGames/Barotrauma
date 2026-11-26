#nullable enable

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplDebugConsole(PluginData data, Assembly assembly) : SimpleRegisteringService<DebugConsole.Command>(data, assembly), IDebugConsole
    {
        private readonly ContentPackage? contentPackage = PluginLoader.TryFindContentPackageForAssembly(assembly);

        public void NewMessage(string message, Color? color = null)
        {
            string msg = DebugConsole.AddAssemblyInfoToMessage(message, PluginAssembly);
            string msgWithContentPackage = DebugConsole.AddContentPackageInfoToMessage(msg, contentPackage);
            DebugConsole.NewMessage(msgWithContentPackage, color);
        }

        public void ThrowError(string message, Exception? exception = null)
        {
            DebugConsole.ThrowError(message, exception, contentPackage, PluginAssembly);
        }

        public void AddWarning(string message)
        {
            DebugConsole.AddWarning(message, contentPackage, PluginAssembly);
        }

#if CLIENT
        public void RegisterCommand(string command, string helpMessage, CommandFlags flags, IDebugConsole.OnCommandExecutedDelegate onCommandExecuted,
            Func<string[][]>? getValidArgs = null)
        {
            DebugConsole.Command newCommand = new(
                name: command,
                help: helpMessage,
                onExecute: args => { onCommandExecuted(args); },
                isCheat: flags.HasFlag(CommandFlags.IsCheat),
                getValidArgs: getValidArgs);

            if (flags.HasFlag(CommandFlags.DoNotRelayToServer))
            {
                newCommand.OnClientExecute = args => { onCommandExecuted(args); };
                newCommand.RelayToServer = false;
            }
            Register(newCommand);
            DebugConsole.Commands.Add(newCommand);
        }
#else
        public void RegisterCommand(string command, string helpMessage, CommandFlags flags, 
            IDebugConsole.OnCommandExecutedDelegate onCommandExecuted, 
            IDebugConsole.OnCommandExecutedByClientDelegate? onCommandExecutedByClient = null,
            Func<string[][]>? getValidArgs = null)
        {
            DebugConsole.Command newCommand = new(
                name: command,
                help: helpMessage,
                onExecute: args => { onCommandExecuted(args); },
                isCheat: flags.HasFlag(CommandFlags.IsCheat),
                getValidArgs: getValidArgs);
            if (onCommandExecutedByClient != null)
            {
                newCommand.OnClientRequestExecute = (client, cursorPos, args) => { onCommandExecutedByClient(client, cursorPos, args); };
            }
            Register(newCommand);
            DebugConsole.Commands.Add(newCommand);
        }
#endif

        public void StartTestMaps(
            string? fixedSeed = null,
            int? amount = null,
            Action<string, Map, CampaignMode>? onMapGenerated = null,
            Action? onBatchFinished = null)
        {
            CoroutineManager.StartCoroutine(
                DebugConsole.TestMaps(fixedSeed, amount, onMapGenerated, onBatchFinished));
        }

        public void DeregisterCommand(string command)
        {
            if (Registered.FirstOrDefault(c => c.Matches(command)) is not { } cmd)
            {
                return;
            }

            Deregister(cmd);
        }

        public override void OnDeregister(DebugConsole.Command item)
        {
            item.Dispose();
            DebugConsole.Commands.Remove(item);
        }
    }
}