#nullable enable
using System;
using Barotrauma.Networking;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Delegate for client side network handlers.
    /// </summary>
    /// <param name="data">Data received from the server.</param>
    /// <typeparam name="T">Struct implementing <see cref="INetSerializableStruct"/> to be deserialized from the received data.</typeparam>
    public delegate void ClientNetworkHandlerDelegate<in T>(T data)
        where T : INetSerializableStruct;

    /// <summary>
    /// Delegate for server side network handlers.
    /// </summary>
    /// <param name="data">Data received from the client.</param>
    /// <param name="client">Client that sent the data.</param>
    /// <typeparam name="T">Struct implementing <see cref="INetSerializableStruct"/> to be deserialized from the received data.</typeparam>
    public delegate void ServerNetworkHandlerDelegate<in T>(T data, Client client)
        where T : INetSerializableStruct;

    public delegate void EntityEventClientReadDelegate<in TNetSerializable>(TNetSerializable entity, IReadMessage msg, float sendingTime) where TNetSerializable : INetSerializable;
    public delegate void EntityEventServerReadDelegate<in TNetSerializable>(TNetSerializable entity, IReadMessage msg, Client client) where TNetSerializable : INetSerializable;
    public delegate void EntityEventClientWriteDelegate<in TNetSerializable>(TNetSerializable entity, IWriteMessage msg, NetEntityEvent.IData? extraData) where TNetSerializable : INetSerializable;
    public delegate void EntityEventServerWriteDelegate<in TNetSerializable>(TNetSerializable entity, IWriteMessage msg, Client client, NetEntityEvent.IData? extraData) where TNetSerializable : INetSerializable;

    /// <summary>
    /// Interface for implementing bidirectional serialization for entities to be registered via <see cref="IGameNetwork.RegisterCustomEntityEventHandler{TSerializable}"/>.
    /// </summary>
    /// <typeparam name="T">Entity type implementing both <see cref="IClientSerializable"/> and <see cref="IServerSerializable"/>.</typeparam>
    public interface ICustomSerializable<in T> where T : IClientSerializable, IServerSerializable
    {
#if CLIENT
        void ClientWrite(T entity, IWriteMessage msg, NetEntityEvent.IData? extraData);
        void ClientRead(T entity, IReadMessage msg, float sendingTime);
#elif SERVER
        void ServerWrite(T entity, IWriteMessage msg, Client client, NetEntityEvent.IData? extraData);
        void ServerRead(T entity, IReadMessage msg, Client client);
#endif
    }

    /// <summary>
    /// Attribute to assign a header to a network handler method.
    /// </summary>
    /// <param name="header">Which header to assign to the method.</param>
    /// <typeparam name="T">Enum type registered via <see cref="IGameNetwork.RegisterNetworkHeaders{TEnum}"/>.</typeparam>
    /// <example>
    /// <code>
    /// [NetworkHandler&lt;MyNetworkHeaders&gt;(MyNetworkHeaders.ExampleHeader1)]
    /// public static void ExampleHandler1(MyData data) { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method)]
    public class NetworkHandlerAttribute<T>(T header) : Attribute where T : Enum
    {
        public readonly T Header = header;
    }

    /// <summary>
    /// Provides a service to allow client and server side network communication in multiplayer.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IGameNetwork GameNetwork = PluginServiceProvider.GetService&lt;IGameNetwork&gt;();
    /// </code>
    /// </remarks>
    public interface IGameNetwork : IPluginService
    {
        /// <summary>
        /// Registers the header enum to be used for network communication.
        /// </summary>
        /// <typeparam name="TEnum">Enum type to register.</typeparam>
        /// <exception cref="InvalidOperationException">If an enum is already registered for the plugin.</exception>
        /// <example>
        /// <code>
        /// public enum MyNetworkHeaders
        /// {
        ///     ExampleHeader1,
        ///     ExampleHeader2
        /// }
        /// </code>
        /// <code>
        /// GameNetwork.RegisterNetworkHeaders&lt;MyNetworkHeaders&gt;();
        /// </code>
        /// </example>
        public void RegisterNetworkHeaders<TEnum>() where TEnum : Enum;

        /// <summary>
        /// Will scan the entire assembly for static methods with the <see cref="NetworkHandlerAttribute{T}"/>
        /// attribute and register them as network handlers.
        /// </summary>
        public void RegisterAllStaticHandlers();

        /// <summary>
        /// Will scan the object instance for methods with the <see cref="NetworkHandlerAttribute{T}"/>
        /// attribute and register them as network handlers.
        /// </summary>
        /// <param name="module">Object instance to register handlers from.</param>
        /// <example>
        /// Create a class that contains network handlers:
        /// <code>
        /// public class MyNetworkHandlers
        /// {
        ///     [NetworkHandler&lt;MyNetworkHeaders&gt;(MyNetworkHeaders.ExampleHeader1)]
        ///     public void ExampleHandler1(MyData data)
        ///     {
        ///         // read data from MyData
        ///     }
        /// }
        /// </code>
        /// Static field:
        /// <code>
        /// public static readonly MyNetworkHandlers MyHandlers = new();
        /// </code>
        /// Plugin init:
        /// <code>
        /// GameNetwork.RegisterHandlers(MyHandlers);
        /// </code>
        /// </example>
        public void RegisterHandlers<T>(T module);

        /// <summary>
        /// Creates a network event for an entity.
        /// Write and read methods for the entity event must be registered via the respective
        /// EntityClientWrite, EntityClientRead, EntityServerWrite and EntityServerRead methods
        /// in this interface.
        /// </summary>
        /// <param name="serializable">Entity to create the event for.</param>
        /// <param name="data">Extra data to pass to the entity event. Can be null if not needed.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="INetSerializable"/>.</typeparam>
        /// <example>
        /// <code>
        /// item.GetExtraField&lt;MyCustomData&gt;("CustomData").Foo = Bar;
        /// GameNetwork.CreateEntityEvent(item, null);
        /// </code>
        /// </example>
        public void CreateEntityEvent<TSerializable>(TSerializable serializable, NetEntityEvent.IData? data)
            where TSerializable : INetSerializable;

        /// <summary>
        /// Creates a network event using the vanilla entity write and read methods.
        /// Useful for example if you are editing a state of a vanilla entity that doesn't
        /// automatically create entity events.
        /// </summary>
        /// <param name="serializable">Entity to create the event for.</param>
        /// <param name="data">Extra data to pass to the entity event. Can be null if not needed.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="INetSerializable"/>.</typeparam>
        public void CreateVanillaEntityEvent<TSerializable>(TSerializable serializable, NetEntityEvent.IData? data)
            where TSerializable : INetSerializable;

        /// <summary>
        /// Registers ClientWrite, ClientRead, ServerWrite and ServerRead entity event handlers for a specific entity.
        /// </summary>
        /// <param name="handler">Instance of a custom entity event handler implementing <see cref="ICustomSerializable{T}"/>.</param>
        /// <typeparam name="TEntity">Entity that registers both <see cref="IClientSerializable"/> and <see cref="IServerSerializable"/>.</typeparam>
        /// <example>
        /// Client project:
        /// <code>
        /// public class CustomItemHandler : ICustomSerializable&lt;Item&gt;
        /// {
        ///     public void ClientWrite(Item item, IWriteMessage msg, NetEntityEvent.IData? extraData)
        ///     {
        ///         MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///         msg.WriteInt32(data.Foo);
        ///     }
        /// 
        ///     public void ClientRead(Item item, IReadMessage msg, float sendingTime)
        ///     {
        ///         MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///         data.Foo = msg.ReadInt32();
        ///     }
        /// }
        /// </code>
        /// Server project:
        /// <code>
        /// public class CustomItemHandler : ICustomSerializable&lt;Item&gt;
        /// {
        ///     public void ServerWrite(Item item, IWriteMessage msg, Client client, NetEntityEvent.IData? extraData)
        ///     {
        ///         MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///         msg.WriteInt32(data.Foo);
        ///     }
        /// 
        ///     public void ServerRead(Item item, IReadMessage msg, Client client)
        ///     {
        ///         MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///         data.Foo = msg.ReadInt32();
        ///     }
        /// }
        /// </code>
        /// Register the handler in shared project:
        /// <code>
        /// GameNetwork.RegisterCustomEntityEventHandler&lt;Item&gt;(new CustomItemHandler());
        /// </code>
        /// </example>
        public void RegisterCustomEntityEventHandler<TEntity>(ICustomSerializable<TEntity> handler)
            where TEntity : IClientSerializable, IServerSerializable;

#if CLIENT
        /// <summary>
        /// Registers a client side event write handler for an entity.
        /// </summary>
        /// <param name="handler">Method or delegate to call when an entity event is created.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="IClientSerializable"/> we are registering the handler for.</typeparam>
        /// <remarks>
        /// The server must have corresponding ServerRead handler registered for the entity.
        /// </remarks>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterEntityClientWrite&lt;Item&gt;((item, msg, extraData) =>
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     msg.WriteInt32(data.Foo);
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterEntityClientWrite&lt;Item&gt;(WriteItem);
        /// </code>
        /// <code>
        /// public void WriteItem(Item item, IWriteMessage msg, NetEntityEvent.IData? extraData)
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     msg.WriteInt32(data.Foo);
        /// }
        /// </code>
        /// </example>
        public void RegisterEntityClientWrite<TSerializable>(EntityEventClientWriteDelegate<TSerializable> handler)
            where TSerializable : IClientSerializable;

        /// <summary>
        /// Registers a client side event read handler for an entity.
        /// </summary>
        /// <param name="handler">Method or delegate to call when an entity event is received.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="IServerSerializable"/> we are registering the handler for.</typeparam>
        /// <remarks>
        /// The server should have corresponding ServerWrite handler registered for the entity.
        /// </remarks>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterEntityClientRead&lt;Item&gt;((item, msg, sendingTime) =>
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     data.Foo = msg.ReadInt32();
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterEntityClientRead&lt;Item&gt;(ReadItem);
        /// </code>
        /// <code>
        /// public void ReadItem(Item item, IReadMessage msg, float sendingTime)
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     data.Foo = msg.ReadInt32();
        /// }
        /// </code>
        /// </example>
        public void RegisterEntityClientRead<TSerializable>(EntityEventClientReadDelegate<TSerializable> handler)
            where TSerializable : IServerSerializable;

        /// <summary>
        /// Manually register a client side handler for a specific network header.
        /// </summary>
        /// <param name="header">Header to register the handler for.</param>
        /// <param name="handler">Method or delegate to call when the header is received.</param>
        /// <typeparam name="TData">Struct implementing <see cref="INetSerializableStruct"/> to be deserialized from the received data.</typeparam>
        /// <typeparam name="TEnum">Enum type registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">A handler for the header is already registered or the enum header is not registered.</exception>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterHandler(MyNetworkHeaders.ExampleHeader1, (MyData myData) =>
        /// {
        ///     // read data from MyData
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterHandler(MyNetworkHeaders.ExampleHeader1, MyHandler);
        /// </code>
        /// <code>
        /// public void MyHandler(MyData myData)
        /// {
        ///     // read data from MyData
        /// }
        /// </code>
        /// </example>
        public void RegisterHandler<TEnum, TData>(TEnum header, ClientNetworkHandlerDelegate<TData> handler)
            where TEnum : Enum
            where TData : INetSerializableStruct;

        /// <summary>
        /// Sends data to the server.
        /// </summary>
        /// <param name="header">Network header to identify the data.</param>
        /// <param name="data">Data to send.</param>
        /// <typeparam name="TData">Struct implementing <see cref="INetSerializableStruct"/> to be serialized and sent.</typeparam>
        /// <typeparam name="TEnum">Enum type registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">Given enum header is not registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</exception>
        /// <remarks>
        /// The server needs to have a handler registered for the enum header to receive the data.
        /// </remarks>
        /// <example>
        /// <code>
        /// [NetworkSerialize]
        /// public readonly record struct MyData(int Foo) : INetSerializableStruct;
        /// </code>
        /// <code>
        /// GameNetwork.Send(MyNetworkHeaders.ExampleHeader1, new MyData(42));
        /// </code>
        /// </example>
        public void Send<TEnum, TData>(TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct;
#elif SERVER
        /// <summary>
        /// Registers a server side event write handler for an entity.
        /// </summary>
        /// <param name="handler">Method or delegate to call when an entity event is created.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="IServerSerializable"/> we are registering the handler for.</typeparam>
        /// <remarks>
        /// The client must have corresponding ClientRead handler registered for the entity.
        /// </remarks>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterEntityServerWrite&lt;Item&gt;((item, msg, client, extraData) =>
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     msg.WriteInt32(data.Foo);
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterEntityServerWrite&lt;Item&gt;(WriteItem);
        /// </code>
        /// <code>
        /// public void WriteItem(Item item, IWriteMessage msg, Client client, NetEntityEvent.IData? extraData)
        /// {
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     msg.WriteInt32(data.Foo);
        /// }
        /// </code>
        /// </example>
        public void RegisterEntityServerWrite<TSerializable>(EntityEventServerWriteDelegate<TSerializable> handler)
            where TSerializable : IServerSerializable;

        /// <summary>
        /// Registers a server side event read handler for an entity.
        /// </summary>
        /// <param name="handler">Method or delegate to call when an entity event is received.</param>
        /// <typeparam name="TSerializable">Entity type implementing <see cref="IClientSerializable"/> we are registering the handler for.</typeparam>
        /// <remarks>
        /// The client should have corresponding ClientWrite handler registered for the entity.
        /// </remarks>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterEntityServerRead&lt;Item&gt;((item, msg, client) =>
        /// {
        ///     // check if the client is allowed to do this first
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     data.Foo = msg.ReadInt32();
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterEntityServerRead&lt;Item&gt;(ReadItem);
        /// </code>
        /// <code>
        /// public void ReadItem(Item item, IReadMessage msg, Client client)
        /// {
        ///     // check if the client is allowed to do this first
        ///     MyCustomData data = item.GetExtraField&lt;MyCustomData&gt;("CustomData");
        ///     data.Foo = msg.ReadInt32();
        /// }
        /// </code>
        /// </example>
        public void RegisterEntityServerRead<TSerializable>(EntityEventServerReadDelegate<TSerializable> handler)
            where TSerializable : IClientSerializable;

        /// <summary>
        /// Manually register a server side handler for a specific network header.
        /// </summary>
        /// <param name="header">Header to register the handler for.</param>
        /// <param name="handler">Method or delegate to call when the header is received.</param>
        /// <typeparam name="TData">Struct implementing <see cref="INetSerializableStruct"/> to be deserialized from the received data.</typeparam>
        /// <typeparam name="TEnum">Enum type registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">A handler for the header is already registered or the enum header is not registered.</exception>
        /// <example>
        /// <code>
        /// GameNetwork.RegisterHandler(MyNetworkHeaders.ExampleHeader1, (MyData myData, Client client) =>
        /// {
        ///     // read data from MyData
        /// });
        /// </code>
        /// or with a method:
        /// <code>
        /// GameNetwork.RegisterHandler(MyNetworkHeaders.ExampleHeader1, MyHandler);
        /// </code>
        /// <code>
        /// public void MyHandler(MyData myData, Client client)
        /// {
        ///     // read data from MyData
        /// }
        /// </code>
        /// </example>
        public void RegisterHandler<TEnum, TData>(TEnum header, ServerNetworkHandlerDelegate<TData> handler)
            where TEnum : Enum
            where TData : INetSerializableStruct;

        /// <summary>
        /// Sends data to all connected clients.
        /// </summary>
        /// <param name="header">Network header to identify the data.</param>
        /// <param name="data">Data to send.</param>
        /// <typeparam name="TEnum">Enum type registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</typeparam>
        /// <typeparam name="TData">Struct implementing <see cref="INetSerializableStruct"/> to be serialized and sent.</typeparam>
        /// <exception cref="InvalidOperationException">Given enum header is not registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</exception>
        /// <remarks>
        /// The client needs to have a handler registered for the enum header to receive the data.
        /// </remarks>
        /// <example>
        /// <code>
        /// [NetworkSerialize]
        /// public readonly record struct MyData(int Foo) : INetSerializableStruct;
        /// </code>
        /// <code>
        /// GameNetwork.Broadcast(MyNetworkHeaders.ExampleHeader1, new MyData(42));
        /// </code>
        /// </example>
        public void Broadcast<TEnum, TData>(TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct;

        /// <summary>
        /// Sends data to a specific client.
        /// </summary>
        /// <param name="client">Client to send the data to.</param>
        /// <param name="header">Network header to identify the data.</param>
        /// <param name="data">Data to send.</param>
        /// <typeparam name="TEnum">Enum type registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</typeparam>
        /// <typeparam name="TData">Struct implementing <see cref="INetSerializableStruct"/> to be serialized and sent.</typeparam>
        /// <exception cref="InvalidOperationException">Given enum header is not registered via <see cref="RegisterNetworkHeaders{TEnum}"/>.</exception>
        /// <remarks>
        /// The client needs to have a handler registered for the enum header to receive the data.
        /// </remarks>
        /// <example>
        /// <code>
        /// [NetworkSerialize]
        /// public readonly record struct MyRequest(int Foo) : INetSerializableStruct;
        /// 
        /// [NetworkSerialize]
        /// public readonly record struct MyResponse(int Bar) : INetSerializableStruct;
        /// </code>
        /// Respond to client in a network handler:
        /// <code>
        /// GameNetwork.RegisterHandler(MyNetworkHeaders.ExampleHeader1, (MyRequest request, Client client) =>
        /// {
        ///     MyResponse response = new(request.Foo * 2);
        ///     GameNetwork.SendToClient(client, MyNetworkHeaders.ExampleHeader2, response);
        /// });
        /// </code>
        /// </example>
        public void SendToClient<TEnum, TData>(Client client, TEnum header, TData data)
            where TEnum : Enum
            where TData : INetSerializableStruct;
#endif
    }
}