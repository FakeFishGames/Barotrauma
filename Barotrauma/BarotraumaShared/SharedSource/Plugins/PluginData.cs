#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Barotrauma.Plugins;
using Barotrauma.Networking;

namespace Barotrauma
{
    /// <summary>
    /// This class contains a collection of different stuff that plugins register that the game should
    /// interact with. Gathering them all into one class makes it easier to manage and dispose of them.
    /// This way we can prevent unload errors due to the way collectible assemblies work.
    ///
    /// In short, unloading a collectible assembly is only a suggestion and the .NET runtime will actually
    /// unload it after it feels like it's safe to do so. For example if we had a list of objects somewhere in the game
    /// and a plugin registered a class that is declared in the collectible assembly and added it to that list,
    /// the assembly will not be unloaded since that would put the program in a state where there exists an object
    /// with a type that is no longer loaded.
    /// </summary>
    internal class PluginData(Assembly pluginAssembly, ContentPackage cp) : IDisposable
    {
        private readonly Assembly pluginAssembly = pluginAssembly;
        public readonly ContentPackage ContentPackage = cp;

        internal static ImmutableArray<PluginData> LoadedPluginData = ImmutableArray<PluginData>.Empty;

        /// <summary>
        /// Recommend checking out the ConditionalWeakTable documentation, it's a very interesting class.
        /// https://docs.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-6.0
        /// </summary>
        public readonly ConditionalWeakTable<object, ExtraFields> StoredFields = new();

        /// <summary>
        /// Content file types which are dynamically registered at runtime by code mods.
        /// </summary>
        public readonly HashSet<ContentFile.TypeInfo> DynamicTypes = [];

        /// <summary>
        /// Cached content files which were loaded dynamically, by <see cref="DynamicallyLoadedFile"/>.
        /// </summary>
        public readonly HashSet<ContentFile> DynamicFiles = [];

        public readonly Dictionary<Type, EntityEventClientReadDelegate<INetSerializable>> EntityEventClientReadHandlers = [];
        public readonly Dictionary<Type, EntityEventServerReadDelegate<INetSerializable>> EntityEventServerReadHandlers = [];
        public readonly Dictionary<Type, EntityEventClientWriteDelegate<INetSerializable>> EntityEventClientWriteHandlers = [];
        public readonly Dictionary<Type, EntityEventServerWriteDelegate<INetSerializable>> EntityEventServerWriteHandlers = [];

        public readonly Dictionary<Type, HashSet<Delegate>> Hooks = [];

        public readonly List<IStatusEffectActionFactory> StatusEffectActions = [];

        public readonly List<IOrderObjectiveFactory> OrderObjectiveFactories = [];

        private static bool cacheInvalidated = true;

        private static ImmutableDictionary<Type, ImmutableHashSet<Delegate>> cachedDelegates
            = ImmutableDictionary<Type, ImmutableHashSet<Delegate>>.Empty;

        internal static List<SettingsService> SettingsServices = [];

        /// <summary>
        /// Collection of registered types that can be loaded from XML.
        /// </summary>
        /// <remarks>
        /// This could be changed to use <see cref="ReflectionUtils.GetTypeWithBackwardsCompatibility"/> and
        /// passing the plugin assembly into it, but I think keeping all these types in PluginData
        /// is better, to prevent any possible unloadability issues and I think it communicates to modders
        /// better by making them manually register their types instead of assuming that just because a class
        /// inherits a certain class it will be automatically registered.
        /// </remarks>
        internal readonly HashSet<Type> ItemComponents = [],
                                        EventActions = [];

        internal readonly Dictionary<Identifier, HashSet<ContentXElement>> AdditionalItemComponents = [];

        internal Option<Type> RegisteredNetworkOpcodeEnum = Option.None;
        internal Range<int> NetworkOpcodeRange = new Range<int>(int.MinValue, int.MaxValue);

        internal readonly Dictionary<Identifier, Action> OnResolutionChangedEvents = [];

        internal readonly record struct NetworkHook(Delegate Hook, Type StructType)
        {
            public bool IsDefault
                => this == default;
        }

        internal readonly Dictionary<int, NetworkHook> RegisteredNetworkHooks = [];

        internal static ImmutableHashSet<T> GetHooks<T>() where T : Delegate
        {
            if (cacheInvalidated)
            {
                CreateCache();
            }

            if (cachedDelegates.TryGetValue(typeof(T), out ImmutableHashSet<Delegate>? cachedDelegate))
            {
                VerifyAllHooksMatch<T>(cachedDelegate);

                // This is kinda hacky, but normally you can't cast a collection from one type to another.
                // Would have to use either Linq or create a new collection, and I want to avoid allocating
                // memory in this method where possible.
                return Unsafe.As<ImmutableHashSet<Delegate>, ImmutableHashSet<T>>(ref cachedDelegate);
            }

            return ImmutableHashSet<T>.Empty;
        }

        // This method is called so infrequently that LINQ is fine here
        internal static IEnumerable<ContentFile.TypeInfo> AllDynamicTypes
            => LoadedPluginData.SelectMany(static pd => pd.DynamicTypes);

        internal static IEnumerable<ContentFile> AllDynamicContentFiles
            => LoadedPluginData.SelectMany(static pd => pd.DynamicFiles);

        [Conditional("DEBUG")]
        private static void VerifyAllHooksMatch<T>(IEnumerable<Delegate> hooks) where T : Delegate
        {
            foreach (Delegate hook in hooks)
            {
                if (hook.GetType() != typeof(T))
                {
                    throw new InvalidOperationException($"Hook {hook} does not match type {typeof(T)}");
                }
            }
        }

        private static void CreateCache()
        {
            Dictionary<Type, HashSet<Delegate>> cacheBuilder = new();

            foreach (PluginData pluginData in LoadedPluginData)
            {
                lock (pluginData.Hooks)
                {
                    foreach ((Type type, HashSet<Delegate> hooks) in pluginData.Hooks)
                    {
                        if (cacheBuilder.TryGetValue(type, out HashSet<Delegate>? cachedHooks))
                        {
                            cachedHooks.UnionWith(hooks);
                        }
                        else
                        {
                            cacheBuilder[type] = new HashSet<Delegate>(hooks);
                        }
                    }
                }
            }

            cachedDelegates =
                cacheBuilder.ToImmutableDictionary(
                    static kvp => kvp.Key,
                    static kvp => kvp.Value.ToImmutableHashSet());
            cacheInvalidated = false;
        }

        internal void AddHook<T>(T hook) where T : Delegate
        {
            lock (Hooks)
            {
                if (Hooks.TryGetValue(typeof(T), out HashSet<Delegate>? hookSet))
                {
                    hookSet.Add(hook);
                    VerifyAllHooksMatch<T>(hookSet);
                }
                else
                {
                    Hooks[typeof(T)] = [hook];
                }

                cacheInvalidated = true;
            }
        }

        internal void RemoveHook<T>(T hook) where T : Delegate
        {
            lock (Hooks)
            {
                if (Hooks.TryGetValue(typeof(T), out HashSet<Delegate>? hookSet))
                {
                    cacheInvalidated = true;
                    hookSet.Remove(hook);
                }
            }
        }

        internal void RemoveAll<T>(IEnumerable<T> hooks) where T : Delegate
        {
            lock (Hooks)
            {
                if (Hooks.TryGetValue(typeof(T), out HashSet<Delegate>? hookSet))
                {
                    foreach (T hook in hooks)
                    {
                        cacheInvalidated = true;
                        hookSet.Remove(hook);
                    }
                }
            }
        }

        public void Dispose()
        {
            cacheInvalidated = true;
            cachedDelegates = ImmutableDictionary<Type, ImmutableHashSet<Delegate>>.Empty;
            StoredFields.Clear();
            Hooks.Clear();
            StatusEffectActions.Clear();
            DynamicTypes.Clear();
        }

        public static void ClientRead(IReadMessage inc)
        {
            if (!NetReadInit(inc, out PluginData? data, out NetworkHook hook)) { return; }

            try
            {
                object? netStruct = INetSerializableStruct.ReadGeneric(hook.StructType, inc);
                hook.Hook.DynamicInvoke(netStruct);
            }
            catch (Exception e)
            {
                var innerException = e.InnerException ?? e;
                DebugConsole.ThrowError($"Error while reading modded network message: {innerException.Message}",
                                        innerException,
                                        pluginAssembly: data.pluginAssembly,
                                        contentPackage: data.ContentPackage);
            }
        }

        public static void ServerRead(IReadMessage inc, Client connectedClient)
        {
            if (!NetReadInit(inc, out PluginData? data, out NetworkHook hook)) { return; }

            try
            {
                object? netStruct = INetSerializableStruct.ReadGeneric(hook.StructType, inc);
                hook.Hook.DynamicInvoke(netStruct, connectedClient);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError($"Error while reading modded network message: {e.Message}",
                                        pluginAssembly: data.pluginAssembly,
                                        contentPackage: data.ContentPackage);
            }
        }

        private static bool NetReadInit(IReadMessage inc, [NotNullWhen(true)] out PluginData? data, out NetworkHook hook)
        {
            uint pluginHash = inc.ReadUInt32();

            if (!PluginLoader.GetPluginDataForHash(pluginHash).TryUnwrap(out var pluginData))
            {
                // Should never happen in practice
                DebugConsole.ThrowError($"Received a message from a plugin with hash {pluginHash} that is not loaded.");
                data = null;
                hook = default;
                return false;
            }
            data = pluginData;

            int headerInt = inc.ReadRangedInteger(pluginData.NetworkOpcodeRange.Start, pluginData.NetworkOpcodeRange.End);

            if (!pluginData.RegisteredNetworkHooks.TryGetValue(headerInt, out var netHook))
            {
                string? headerName = headerInt.ToString();
                if (pluginData.RegisteredNetworkOpcodeEnum.TryUnwrap(out Type? enumType))
                {
                    if (Enum.IsDefined(enumType, headerInt))
                    {
                        headerName = Enum.GetName(enumType, headerInt);
                    }
                }

                DebugConsole.ThrowError($"Received a message with header \"{headerName}\" with no registered handler.",
                                        pluginAssembly: pluginData.pluginAssembly,
                                        contentPackage: pluginData.ContentPackage);
                hook = default;
                return false;
            }

            hook = netHook;
            return true;
        }

        public static void ReadClientEvent(uint contentPackageHash, IReadMessage msg, INetSerializable entity, float sendingTime)
        {
            if (!PluginLoader.GetPluginDataForHash(contentPackageHash).TryUnwrap(out PluginData? pluginData))
            {
                // Should never happen in practice
                DebugConsole.ThrowError($"Received an event from a plugin with hash {contentPackageHash} that is not loaded.");
                return;
            }

            Type entityType = entity.GetType();
            if (pluginData.EntityEventClientReadHandlers.TryGetValue(entityType, out EntityEventClientReadDelegate<INetSerializable>? handler))
            {
                try
                {
                    handler.Invoke(entity, msg, sendingTime);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while reading modded entity event: {e.Message}",
                                            pluginAssembly: pluginData.pluginAssembly,
                                            contentPackage: pluginData.ContentPackage);
                }
            }
            else
            {
                DebugConsole.ThrowError($"Received an event for entity type {entityType} with no registered read handler.",
                                        pluginAssembly: pluginData.pluginAssembly,
                                        contentPackage: pluginData.ContentPackage);
            }
        }

        public static void ReadServerEvent(uint contentPackageHash, IReadMessage msg, INetSerializable entity, Client client)
        {
            if (!PluginLoader.GetPluginDataForHash(contentPackageHash).TryUnwrap(out PluginData? pluginData))
            {
                // Should never happen in practice
                DebugConsole.ThrowError($"Received an event from a plugin with hash {contentPackageHash} that is not loaded.");
                return;
            }

            Type entityType = entity.GetType();
            if (pluginData.EntityEventServerReadHandlers.TryGetValue(entityType, out EntityEventServerReadDelegate<INetSerializable>? handler))
            {
                try
                {
                    handler.Invoke(entity, msg, client);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while reading modded entity event: {e.Message}",
                                            pluginAssembly: pluginData.pluginAssembly,
                                            contentPackage: pluginData.ContentPackage);
                }
            }
            else
            {
                DebugConsole.ThrowError($"Received an event for entity type {entityType} with no registered read handler.",
                                        pluginAssembly: pluginData.pluginAssembly,
                                        contentPackage: pluginData.ContentPackage);
            }
        }

        public static void WriteEvent(uint contentPackageHash, IWriteMessage tempEventBuffer, NetEntityEvent netEntityEvent, Client? recipient)
        {
            if (!PluginLoader.GetPluginDataForHash(contentPackageHash).TryUnwrap(out PluginData? pluginData))
            {
                // Should never happen in practice
                DebugConsole.ThrowError($"Received an event from a plugin with hash {contentPackageHash} that is not loaded.");
                return;
            }

            Type entityType = netEntityEvent.Entity.GetType();
#if CLIENT
            if (pluginData.EntityEventClientWriteHandlers.TryGetValue(entityType, out EntityEventClientWriteDelegate<INetSerializable>? handler))
            {
                if (netEntityEvent is not ClientEntityEvent clientEvent) { return; }

                try
                {
                    tempEventBuffer.WriteUInt16(clientEvent.CharacterStateID);
                    handler.Invoke(clientEvent.Serializable, tempEventBuffer, clientEvent.Data);
                    clientEvent.Sent = true;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while writing modded entity event: {e.Message}",
                                            pluginAssembly: pluginData.pluginAssembly,
                                            contentPackage: pluginData.ContentPackage);
                }
            }
#elif SERVER
            if (pluginData.EntityEventServerWriteHandlers.TryGetValue(entityType, out EntityEventServerWriteDelegate<INetSerializable>? handler))
            {
                if (netEntityEvent is not ServerEntityEvent serverEvent) { return; }

                try
                {
                    handler.Invoke(serverEvent.Serializable, tempEventBuffer, recipient!, serverEvent.Data);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while writing modded entity event: {e.Message}",
                                            pluginAssembly: pluginData.pluginAssembly,
                                            contentPackage: pluginData.ContentPackage);
                }
            }
#endif
            else
            {
                DebugConsole.ThrowError($"Received an event for entity type {entityType} with no registered read handler.",
                                        pluginAssembly: pluginData.pluginAssembly,
                                        contentPackage: pluginData.ContentPackage);
            }
        }
    }
}