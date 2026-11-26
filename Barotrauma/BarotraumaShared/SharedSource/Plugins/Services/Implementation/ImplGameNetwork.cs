#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Barotrauma.Networking;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplGameNetwork(PluginData data, Assembly assembly) : IGameNetwork, ITransientService
    {
        public Assembly PluginAssembly { get; } = assembly;
        public PluginData PluginData { get; } = data;

        private readonly HashSet<int> registeredHandlers = [];
        private readonly HashSet<Type> registeredEntityEventHandlers = [];
        private bool hasRegisteredHeaders;

        public void RegisterNetworkHeaders<TEnum>() where TEnum : Enum
        {
            if (PluginData.RegisteredNetworkOpcodeEnum.IsSome())
            {
                throw new InvalidOperationException("Headers already registered for this plugin.");
            }

            hasRegisteredHeaders = true;

            PluginData.RegisteredNetworkOpcodeEnum = Option.Some(typeof(TEnum));

            Array values = Enum.GetValues(typeof(TEnum));
            int[] intValues = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                intValues[i] = Convert.ToInt32(values.GetValue(i));
            }

            PluginData.NetworkOpcodeRange = new Range<int>(intValues.Min(), intValues.Max());
        }

        public void RegisterAllStaticHandlers()
        {
            foreach (Type type in PluginAssembly.GetTypes())
            {
                RegisterTypesInModule(type, module: null);
            }
        }

        public void RegisterHandlers<T>(T module)
            => RegisterTypesInModule(typeof(T), module);

        public void CreateEntityEvent<TSerializable>(TSerializable serializable, NetEntityEvent.IData? data)
            where TSerializable : INetSerializable
        {
            if (!PluginLoader.TryFindMetadataForAssembly(PluginAssembly).TryUnwrap(out PluginMetadata metadata))
            {
                DebugConsole.ThrowError("Failed to find metadata for calling assembly.", pluginAssembly: PluginAssembly);
                return;
            }

            GameMain.NetworkMember?.CreateEntityEvent(serializable, data, pluginContentPackageHash: Option.Some(metadata.IdentifierHash));
        }

        public void CreateVanillaEntityEvent<TSerializable>(TSerializable serializable, NetEntityEvent.IData? data) where TSerializable : INetSerializable
            => GameMain.NetworkMember?.CreateEntityEvent(serializable, data, pluginContentPackageHash: Option.None);

        public void RegisterCustomEntityEventHandler<TEntity>(ICustomSerializable<TEntity> handler)
            where TEntity : IClientSerializable, IServerSerializable
        {
#if CLIENT
            RegisterEntityClientWrite<TEntity>(handler.ClientWrite);
            RegisterEntityClientRead<TEntity>(handler.ClientRead);
#elif SERVER
            RegisterEntityServerWrite<TEntity>(handler.ServerWrite);
            RegisterEntityServerRead<TEntity>(handler.ServerRead);
#endif
        }

        private void RegisterTypesInModule(Type type, object? module)
        {
            BindingFlags flags = module is null
                                     ? BindingFlags.Static | BindingFlags.Public
                                     : BindingFlags.Static
                                       | BindingFlags.Instance
                                       | BindingFlags.NonPublic
                                       | BindingFlags.Public;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                Attribute? attribute = method.GetCustomAttribute(typeof(NetworkHandlerAttribute<>));
                if (attribute is null) { continue; }

                // "Header"
                const string headerName = nameof(NetworkHandlerAttribute<Unit>.Header);

                if (attribute
                    .GetType()
                    .GetField(headerName)?
                    .GetValue(attribute) is not Enum header)
                {
                    continue;
                }

                int intValue = GetIntValue(header);

                if (PluginData.RegisteredNetworkHooks.ContainsKey(intValue))
                {
                    DebugConsole.ThrowError($"Handler for header \"{header}\" already registered.", pluginAssembly: PluginAssembly);
                    continue;
                }

                Type? delegateType = method.GetParameters().FirstOrDefault()?.ParameterType;
                if (delegateType is null)
                {
                    DebugConsole.ThrowError($"No parameter found for method {method}.", pluginAssembly: PluginAssembly);
                    continue;
                }

                Delegate hookDelegate;
                try
                {
                    object? targetModule = method.IsStatic ? null : module;

                    hookDelegate
#if CLIENT
                        = method.CreateDelegate(typeof(ClientNetworkHandlerDelegate<>).MakeGenericType(delegateType), targetModule);
#elif SERVER
                        = method.CreateDelegate(typeof(ServerNetworkHandlerDelegate<>).MakeGenericType(delegateType), targetModule);
#endif
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Failed to create network delegate for method {method}. The method signature is most likely not compatible.", e, pluginAssembly: PluginAssembly);
                    continue;
                }

                PluginData.RegisteredNetworkHooks.Add(intValue, new PluginData.NetworkHook(hookDelegate, delegateType));
            }
        }

#if CLIENT
        public void RegisterEntityClientWrite<TSerializable>(EntityEventClientWriteDelegate<TSerializable> handler)
            where TSerializable : IClientSerializable
        {
            registeredEntityEventHandlers.Add(typeof(TSerializable));
            PluginData.EntityEventClientWriteHandlers[typeof(TSerializable)]
                = (entity, msg, extraData) => handler((TSerializable)entity, msg, extraData);
        }

        public void RegisterEntityClientRead<TSerializable>(EntityEventClientReadDelegate<TSerializable> handler)
            where TSerializable : IServerSerializable
        {
            registeredEntityEventHandlers.Add(typeof(TSerializable));
            PluginData.EntityEventClientReadHandlers[typeof(TSerializable)]
                = (entity, msg, sendingTime) => handler((TSerializable)entity, msg, sendingTime);
        }

        public void RegisterHandler<TEnum, TData>(TEnum header, ClientNetworkHandlerDelegate<TData> handler)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            int intValue = GetIntValue(header);

            if (!PluginData.RegisteredNetworkHooks.TryAdd(intValue, new PluginData.NetworkHook(handler, typeof(TData))))
            {
                throw new InvalidOperationException($"Handler for header {header} already registered.");
            }

            registeredHandlers.Add(intValue);
        }

        public void Send<TEnum, TData>(TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            IWriteMessage msg = Write(header, data);
            GameMain.Client?.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
        }
#elif SERVER
        public void RegisterEntityServerWrite<TSerializable>(EntityEventServerWriteDelegate<TSerializable> handler)
            where TSerializable : IServerSerializable
        {
            registeredEntityEventHandlers.Add(typeof(TSerializable));
            PluginData.EntityEventServerWriteHandlers[typeof(TSerializable)]
                = (entity, msg, client, extraData) => handler((TSerializable)entity, msg, client, extraData);
        }

        public void RegisterEntityServerRead<TSerializable>(EntityEventServerReadDelegate<TSerializable> handler)
            where TSerializable : IClientSerializable
        {
            registeredEntityEventHandlers.Add(typeof(TSerializable));
            PluginData.EntityEventServerReadHandlers[typeof(TSerializable)]
                = (entity, msg, client) => handler((TSerializable)entity, msg, client);
        }

        public void RegisterHandler<TEnum, TData>(TEnum header, ServerNetworkHandlerDelegate<TData> handler)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            int intValue = GetIntValue(header);
            if (!PluginData.RegisteredNetworkHooks.TryAdd(intValue, new PluginData.NetworkHook(handler, typeof(TData))))
            {
                throw new InvalidOperationException($"Handler for header {header} already registered.");
            }

            registeredHandlers.Add(intValue);
        }

        public void Broadcast<TEnum, TData>(TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            if (GameMain.Server?.ConnectedClients is not { } connectedClients) { return; }

            foreach (Client client in connectedClients)
            {
                SendToClient(client, header, data);
            }
        }

        public void SendToClient<TEnum, TData>(Client client, TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            IWriteMessage msg = Write(header, data);
            GameMain.Server?.ServerPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
        }
#endif

        private IWriteMessage Write<TEnum, TData>(TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct
        {
            if (!PluginLoader.TryFindMetadataForAssembly(PluginAssembly).TryUnwrap(out var metadata))
            {
                // Should never happen in practice
                throw new InvalidOperationException("Metadata not found for assembly.");
            }

            IWriteMessage msg = new WriteOnlyMessage()
#if CLIENT
                .WithHeader(ClientPacketHeader.PLUGIN_CUSTOM);
#elif SERVER
                .WithHeader(ServerPacketHeader.PLUGIN_CUSTOM);
#endif

            Range<int> enumRange = PluginData.NetworkOpcodeRange;
            msg.WriteUInt32(metadata.IdentifierHash);
            msg.WriteRangedInteger(GetIntValue(header), enumRange.Start, enumRange.End);
            data.Write(msg);
            return msg;
        }

        private int GetIntValue<TEnum>(TEnum value) where TEnum : Enum
        {
            Type underlyingType = value.GetType().UnderlyingSystemType;
            if (PluginData.RegisteredNetworkOpcodeEnum.TryUnwrap(out var type))
            {
                if (type != underlyingType)
                {
                    throw new InvalidOperationException($"Headers are registered for type {type} but {typeof(TEnum)} was provided.");
                }
            }
            else
            {
                throw new InvalidOperationException("Headers not registered. Call RegisterHeaders before sending data.");
            }

            int intVal = Convert.ToInt32(value);

            Range<int> enumRange = PluginData.NetworkOpcodeRange;
            if (!enumRange.Contains(intVal))
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} is not within the range of the registered enum. {enumRange.Start} <= {value} <= {enumRange.End}");
            }

            return intVal;
        }

        /// <remarks>
        /// Cleanup is very crude in this interface because we only allow one IGameNetwork instance per plugin
        /// so we just need to make sure that we don't clean up the entire PluginData if the service gets disposed
        /// when it hasn't registered anything. For example in <see cref="PluginLoader.AddTransientService"/> when
        /// the service gets immediately disposed if trying to register it twice.
        /// </remarks>
        public void Dispose()
        {
            if (hasRegisteredHeaders)
            {
                PluginData.RegisteredNetworkOpcodeEnum = Option.None;
                PluginData.NetworkOpcodeRange = new Range<int>(int.MinValue, int.MaxValue);
            }

            foreach (Type type in registeredEntityEventHandlers)
            {
                PluginData.EntityEventClientWriteHandlers.Remove(type);
                PluginData.EntityEventClientReadHandlers.Remove(type);
                PluginData.EntityEventServerWriteHandlers.Remove(type);
                PluginData.EntityEventServerReadHandlers.Remove(type);
            }

            foreach (int handler in registeredHandlers)
            {
                PluginData.RegisteredNetworkHooks.Remove(handler);
            }
        }
    }
}