#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// Marks fields and properties as to be serialized and deserialized by <see cref="INetSerializableStruct"/>.
    /// Also contains settings for some types like maximum and minimum values for numbers to reduce bits used.
    /// </summary>
    /// <example>
    /// <code>
    /// struct NetPurchasedItem : INetSerializableStruct
    /// {
    ///     [NetworkSerialize]
    ///     public string Identifier;
    ///
    ///     [NetworkSerialize(ArrayMaxSize = 16)]
    ///     public string[] Tags;
    ///
    ///     [NetworkSerialize(MinValueInt = 0, MaxValueInt = 8)]
    ///     public int Amount;
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Using the attribute on the struct will make all fields and properties serialized
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Struct | AttributeTargets.Property)]
    public class NetworkSerialize : Attribute
    {
        public int MaxValueInt = int.MaxValue;
        public int MinValueInt = int.MinValue;
        public float MaxValueFloat = float.MaxValue;
        public float MinValueFloat = float.MinValue;
        public int NumberOfBits = 8;
        public bool IncludeColorAlpha = false;
        public int ArrayMaxSize = ushort.MaxValue;
    }

    /// <summary>
    /// Static class that contains serialize and deserialize functions for different types used in <see cref="INetSerializableStruct"/>
    /// </summary>
    public static class NetSerializableProperties
    {
        public readonly struct ReadWriteBehavior
        {
            public delegate dynamic? ReadDelegate(IReadMessage inc, Type type, NetworkSerialize attribute);
            public delegate void WriteDelegate(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg);

            public readonly ReadDelegate ReadAction;
            public readonly WriteDelegate WriteAction;

            public ReadWriteBehavior(ReadDelegate readAction, WriteDelegate writeAction)
            {
                ReadAction = readAction;
                WriteAction = writeAction;
            }
        }

        private static readonly ImmutableDictionary<Type, ReadWriteBehavior> TypeBehaviors = new Dictionary<Type, ReadWriteBehavior>
        {
            { typeof(Boolean), new ReadWriteBehavior(ReadBoolean, WriteDynamic) },
            { typeof(Byte), new ReadWriteBehavior(ReadByte, WriteDynamic) },
            { typeof(UInt16), new ReadWriteBehavior(ReadUInt16, WriteDynamic) },
            { typeof(Int16), new ReadWriteBehavior(ReadInt16, WriteDynamic) },
            { typeof(UInt32), new ReadWriteBehavior(ReadUInt32, WriteDynamic) },
            { typeof(Int32), new ReadWriteBehavior(ReadInt32, WriteInt32) },
            { typeof(UInt64), new ReadWriteBehavior(ReadUInt64, WriteDynamic) },
            { typeof(Int64), new ReadWriteBehavior(ReadInt64, WriteDynamic) },
            { typeof(Single), new ReadWriteBehavior(ReadSingle, WriteSingle) },
            { typeof(Double), new ReadWriteBehavior(ReadDouble, WriteDynamic) },
            { typeof(String), new ReadWriteBehavior(ReadString, WriteDynamic) },
            { typeof(Identifier), new ReadWriteBehavior(ReadIdentifier, WriteDynamic) },
            { typeof(Color), new ReadWriteBehavior(ReadColor, WriteColor) },
            { typeof(Vector2), new ReadWriteBehavior(ReadVector2, WriteVector2) }
        }.ToImmutableDictionary();

        private static readonly ReadWriteBehavior InvalidReadWriteBehavior = new ReadWriteBehavior(ReadInvalid, WriteInvalid);

        private static readonly ImmutableDictionary<Predicate<Type>, ReadWriteBehavior> TypePredicates = new Dictionary<Predicate<Type>, ReadWriteBehavior>
        {
            // Arrays
            { type => typeof(Array).IsAssignableFrom(type.BaseType), new ReadWriteBehavior(ReadArray, WriteArray) },

            // Nested INetSerializableStructs
            { type => typeof(INetSerializableStruct).IsAssignableFrom(type), new ReadWriteBehavior(ReadINetSerializableStruct, WriteINetSerializableStruct) },

            // Enums
            { type => type.IsEnum, new ReadWriteBehavior(ReadEnum, WriteEnum) },

            // Nullable
            { type => Nullable.GetUnderlyingType(type) != null, new ReadWriteBehavior(ReadNullable, WriteNullable) },

            // Option
            { type => type.GetGenericTypeDefinition() == typeof(Option<>), new ReadWriteBehavior(ReadOption, WriteOption) }
        }.ToImmutableDictionary();

        private static readonly Dictionary<Type, MethodInfo> cachedSomeCreateMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> cachedNoneCreateMethod = new Dictionary<Type, MethodInfo>();

        private static void WriteInvalid(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg) => throw new InvalidOperationException($"Type {obj?.GetType()} cannot be serialized. Did you forget to implement INetSerializableStruct?");

        private static dynamic ReadInvalid(IReadMessage inc, Type type, NetworkSerialize attribute) => throw new InvalidOperationException($"Type {type} cannot be deserialized. Did you forget to implement INetSerializableStruct?");

        private static void WriteOption(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            Type type = obj.GetType();
            Type optionType = type.GetGenericTypeDefinition();
            Type underlyingType = type.GetGenericArguments()[0];

            if (optionType == typeof(None<>))
            {
                msg.Write(false);
            }
            else if (optionType == typeof(Some<>))
            {
                msg.Write(true);
                if (TryFindBehavior(underlyingType, out ReadWriteBehavior behavior))
                {
                    behavior.WriteAction(obj.Value, attribute, msg);
                }
            }
            else
            {
                throw new InvalidOperationException("Option type was neither None<> or Some<>");
            }
        }

        private static dynamic? ReadOption(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            Type underlyingType = type.GetGenericArguments()[0];
            bool hasValue = inc.ReadBoolean();
            if (!hasValue)
            {
                return GetCreateMethod(typeof(None<>), underlyingType, cachedNoneCreateMethod).Invoke(null, null);
            }

            if (TryFindBehavior(underlyingType, out ReadWriteBehavior behavior))
            {
                dynamic? value = behavior.ReadAction(inc, underlyingType, attribute);
                return GetCreateMethod(typeof(Some<>), underlyingType, cachedSomeCreateMethods).Invoke(null, new []{ value });
            }

            throw new InvalidOperationException($"Could not find suitable behavior for type {underlyingType} in {nameof(ReadOption)}");

            static MethodInfo GetCreateMethod(Type optionType, Type type, Dictionary<Type, MethodInfo> cache)
            {
                if (cache.TryGetValue(type, out MethodInfo? foundInfo))
                {
                    return foundInfo;
                }

                Type genericType = optionType.MakeGenericType(type);
                MethodInfo info = genericType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;
                cache.Add(type, info);
                return info;
            }
        }

        private static void WriteNullable(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is { } notNull)
            {
                msg.Write(true);

                if (TryFindBehavior(notNull.GetType(), out ReadWriteBehavior behavior))
                {
                    // uh oh, something terrible has happened!
                    if (behavior.WriteAction == WriteNullable) { behavior = InvalidReadWriteBehavior; }

                    behavior.WriteAction(notNull, attribute, msg);
                    return;
                }
            }

            msg.Write(false);
        }

        private static dynamic? ReadNullable(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            if (!inc.ReadBoolean()) { return null; }

            Type? underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType is null) { throw new InvalidOperationException($"Could not get the underlying type of {type} in {nameof(ReadNullable)}"); }

            if (TryFindBehavior(underlyingType, out ReadWriteBehavior behavior))
            {
                // uh oh, something terrible has happened!
                if (behavior.ReadAction == ReadNullable) { behavior = InvalidReadWriteBehavior; }

                return behavior.ReadAction(inc, underlyingType, attribute);
            }

            throw new InvalidOperationException($"Could not find suitable behavior for type {underlyingType} in {nameof(ReadNullable)}");
        }

        private static void WriteEnum(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            Range<int> range = GetEnumRange(obj.GetType());
            msg.WriteRangedInteger(Convert.ChangeType(obj, obj.GetTypeCode()), range.Start, range.End);
        }

        private static dynamic ReadEnum(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            Range<int> range = GetEnumRange(type);
            int enumIndex = inc.ReadRangedInteger(range.Start, range.End);

            foreach (dynamic? e in Enum.GetValues(type))
            {
                if (Convert.ChangeType(e, e!.GetTypeCode()) == enumIndex) { return e; }
            }

            throw new InvalidOperationException($"An enum {type} with value {enumIndex} could not be found in {nameof(ReadEnum)}");
        }

        private static void WriteINetSerializableStruct(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            if (!(obj is INetSerializableStruct serializableStruct)) { throw new InvalidOperationException($"Object in {nameof(WriteINetSerializableStruct)} was {obj.GetType()} but expected {nameof(INetSerializableStruct)}"); }

            serializableStruct.Write(msg);
        }

        private static dynamic ReadINetSerializableStruct(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            return INetSerializableStruct.ReadDynamic(type, inc);
        }

        private static void WriteDynamic(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            msg.Write(obj);
        }

        private static dynamic ReadArray(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            Type? elementType = type.GetElementType();
            if (elementType is null) { throw new InvalidOperationException($"Could not get the element type of {type} in {nameof(ReadArray)}"); }

            int length = inc.ReadRangedInteger(0, attribute.ArrayMaxSize);

            Array list = Array.CreateInstance(elementType, length);

            for (int i = 0; i < length; i++)
            {
                if (TryFindBehavior(elementType, out ReadWriteBehavior behavior))
                {
                    list.SetValue(behavior.ReadAction(inc, elementType, attribute), i);
                }
                else
                {
                    throw new InvalidOperationException($"Could not find suitable behavior for type {elementType} in {nameof(ReadArray)}");
                }
            }

            return list;
        }

        private static void WriteArray(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            if (!(obj is Array array)) { throw new InvalidOperationException($"Object in {nameof(WriteArray)} was {obj.GetType()} but expected {nameof(Array)}"); }

            msg.WriteRangedInteger(array.Length, 0, attribute.ArrayMaxSize);

            foreach (dynamic? o in array)
            {
                if (TryFindBehavior(o!.GetType(), out ReadWriteBehavior behavior))
                {
                    behavior.WriteAction(o, attribute, msg);
                }
            }
        }

        private static dynamic ReadBoolean(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadBoolean();

        private static dynamic ReadByte(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadByte();

        private static dynamic ReadUInt16(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadUInt16();

        private static dynamic ReadInt16(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadInt16();

        private static dynamic ReadUInt32(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadUInt32();

        private static dynamic ReadInt32(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            if (IsRanged(attribute.MinValueInt, attribute.MaxValueInt))
            {
                return inc.ReadRangedInteger(attribute.MinValueInt, attribute.MaxValueInt);
            }

            return inc.ReadInt32();
        }

        private static void WriteInt32(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            if (IsRanged(attribute.MinValueInt, attribute.MaxValueInt))
            {
                msg.WriteRangedInteger(obj, attribute.MinValueInt, attribute.MaxValueInt);
                return;
            }

            msg.Write(obj);
        }

        private static dynamic ReadUInt64(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadUInt64();

        private static dynamic ReadInt64(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadInt64();

        private static dynamic ReadSingle(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                return inc.ReadRangedSingle(attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
            }

            return inc.ReadSingle();
        }

        private static void WriteSingle(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                msg.WriteRangedSingle(obj, attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
                return;
            }

            msg.Write(obj);
        }

        private static dynamic ReadDouble(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadDouble();

        private static dynamic ReadString(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadString();
        
        private static dynamic ReadIdentifier(IReadMessage inc, Type type, NetworkSerialize attribute) => inc.ReadIdentifier();

        private static dynamic ReadColor(IReadMessage inc, Type type, NetworkSerialize attribute) => attribute.IncludeColorAlpha ? inc.ReadColorR8G8B8A8() : inc.ReadColorR8G8B8();

        private static void WriteColor(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            if (attribute.IncludeColorAlpha)
            {
                msg.WriteColorR8G8B8A8(obj);
                return;
            }

            msg.WriteColorR8G8B8(obj);
        }

        private static dynamic ReadVector2(IReadMessage inc, Type type, NetworkSerialize attribute)
        {
            float x;
            float y;

            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                x = inc.ReadRangedSingle(attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
                y = inc.ReadRangedSingle(attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
            }
            else
            {
                x = inc.ReadSingle();
                y = inc.ReadSingle();
            }

            return new Vector2(x, y);
        }

        private static void WriteVector2(dynamic? obj, NetworkSerialize attribute, IWriteMessage msg)
        {
            if (obj is null) { throw new ArgumentNullException(nameof(obj), "Tried to write 'null' into a non-nullable type"); }

            var (x, y) = (Vector2)obj;
            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                msg.WriteRangedSingle(x, attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
                msg.WriteRangedSingle(y, attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
                return;
            }

            msg.Write(x);
            msg.Write(y);
        }

        private static bool IsRanged(float minValue, float maxValue) => minValue > float.MinValue || maxValue < float.MaxValue;
        private static bool IsRanged(int minValue, int maxValue) => minValue > int.MinValue || maxValue < int.MaxValue;

        private static Range<int> GetEnumRange(Type type)
        {
            ImmutableArray<int> values = Enum.GetValues(type).Cast<int>().ToImmutableArray();
            return new Range<int>(values.Min(), values.Max());
        }

        public static bool TryFindBehavior(Type type, out ReadWriteBehavior behavior)
        {
            if (TypeBehaviors.TryGetValue(type, out behavior)) { return true; }

            foreach (var (predicate, behavior2) in TypePredicates)
            {
                if (predicate(type))
                {
                    behavior = behavior2;
                    return true;
                }
            }

            behavior = InvalidReadWriteBehavior;
            return false;
        }
    }

    /// <summary>
    /// Interface that allows the creation of automatically serializable and deserializable structs.
    /// <br/><br/>
    /// </summary>
    /// <example>
    /// <code>
    /// public enum PurchaseResult
    /// {
    ///     Unknown,
    ///     Completed,
    ///     Declined
    /// }
    ///
    /// [NetworkSerialize]
    /// struct NetStoreTransaction : INetSerializableStruct
    /// {
    ///     public long Timestamp { get; set; }
    ///     public PurchaseResult Result { get; set; }
    ///     public NetPurchasedItem? PurchasedItem { get; set; }
    /// }
    ///
    /// [NetworkSerialize]
    /// struct NetPurchasedItem : INetSerializableStruct
    /// {
    ///     public string Identifier;
    ///     public string[] Tags;
    ///     public int Amount;
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Supported types are:<br/>
    /// <see cref="Boolean">bool</see><br/>
    /// <see cref="Byte">byte</see><br/>
    /// <see cref="UInt16">ushort</see><br/>
    /// <see cref="Int16">short</see><br/>
    /// <see cref="UInt32">uint</see><br/>
    /// <see cref="Int32">int</see><br/>
    /// <see cref="UInt64">ulong</see><br/>
    /// <see cref="Int64">long</see><br/>
    /// <see cref="Single">float</see><br/>
    /// <see cref="Double">double</see><br/>
    /// <see cref="String">string</see><br/>
    /// <see cref="Microsoft.Xna.Framework.Color"/><br/>
    /// <see cref="Microsoft.Xna.Framework.Vector2"/><br/>
    /// In addition arrays, enums, <see cref="Nullable{T}"/> and <see cref="Option{T}"/> are supported.<br/>
    /// Using <see cref="Nullable{T}"/> or <see cref="Option{T}"/> will make the field or property optional.
    /// </remarks>
    /// <seealso cref="NetworkSerialize"/>
    public interface INetSerializableStruct
    {
        /// <summary>
        /// Deserializes a network message into a struct.
        /// </summary>
        /// <example>
        /// <code>
        /// public void ClientRead(IReadMessage inc)
        /// {
        ///     NetStoreTransaction transaction = INetSerializableStruct.Read&lt;NetStoreTransaction&gt;(inc);
        ///     if (transaction.Result == PurchaseResult.Declined)
        ///     {
        ///         Console.WriteLine("Purchase declined!");
        ///         return;
        ///     }
        ///
        ///     if (transaction.PurchasedItem is { } item)
        ///     {
        ///         // Purchased 3x Wrench with tags: smallitem, mechanical, tool
        ///         Console.WriteLine($"Purchased {item.Amount}x {item.Identifier} with tags: {string.Join(", ", item.Tags)}");
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <param name="inc">Incoming network message</param>
        /// <typeparam name="T">Type of the struct that implements <see cref="INetSerializableStruct"/></typeparam>
        /// <returns>A new struct of type T with fields and properties deserialized</returns>
        public static T Read<T>(IReadMessage inc) where T : INetSerializableStruct => (T)ReadDynamic(typeof(T), inc);

        public static dynamic ReadDynamic(Type type, IReadMessage inc)
        {
            object? newObject = Activator.CreateInstance(type);
            if (newObject is null) { return default!; }

            PropertyInfo[] properties = type.GetProperties();
            foreach (PropertyInfo info in properties)
            {
                NetworkSerialize? attribute = GetAttribute(info, newObject);
                if (attribute is null) { continue; }

                if (NetSerializableProperties.TryFindBehavior(info.PropertyType, out var behavior))
                {
                    object? value = behavior.ReadAction(inc, info.PropertyType, attribute);
                    info.SetValue(newObject, value);
                }
                else
                {
                    DebugConsole.ThrowError($"Unsupported property type \"{info.PropertyType}\" in {newObject}!");
                }
            }

            FieldInfo[] fields = type.GetFields();
            foreach (FieldInfo info in fields)
            {
                NetworkSerialize? attribute = GetAttribute(info, newObject);
                if (attribute is null) { continue; }

                if (NetSerializableProperties.TryFindBehavior(info.FieldType, out var behavior))
                {
                    object? value = behavior.ReadAction(inc, info.FieldType, attribute);
                    info.SetValue(newObject, value);
                }
                else
                {
                    DebugConsole.ThrowError($"Unsupported field type \"{info.FieldType}\" in {newObject}!");
                }
            }

            return newObject;
        }

        /// <summary>
        /// Serializes the struct into a network message
        /// <example>
        /// <code>
        /// public void ServerWrite(IWriteMessage msg)
        /// {
        ///     INetSerializableStruct transaction = new NetStoreTransaction
        ///     {
        ///         Result = PurchaseResult.Completed,
        ///         Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
        ///         PurchasedItem = new NetPurchasedItem
        ///         {
        ///             Identifier = "Wrench",
        ///             Amount = 3,
        ///             Tags = new []{ "smallitem", "mechanical", "tool" }
        ///         }
        ///     };
        ///
        ///     transaction.Write(msg);
        /// }
        /// </code>
        /// </example>
        /// </summary>
        /// <param name="msg">Outgoing network message</param>
        public void Write(IWriteMessage msg)
        {
            PropertyInfo[] properties = GetType().GetProperties();
            foreach (PropertyInfo info in properties)
            {
                NetworkSerialize? attribute = GetAttribute(info, this);
                if (attribute is null) { continue; }

                if (NetSerializableProperties.TryFindBehavior(info.PropertyType, out var behavior))
                {
                    behavior.WriteAction(info.GetValue(this), attribute, msg);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported property type \"{info.PropertyType}\" in {this}");
                }
            }

            FieldInfo[] fields = GetType().GetFields();
            foreach (FieldInfo info in fields)
            {
                NetworkSerialize? attribute = GetAttribute(info, this);
                if (attribute is null) { continue; }

                if (NetSerializableProperties.TryFindBehavior(info.FieldType, out var behavior))
                {
                    behavior.WriteAction(info.GetValue(this), attribute, msg);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported field type \"{info.FieldType}\" in {this}");
                }
            }
        }

        private static NetworkSerialize? GetAttribute(MemberInfo info, object baseClass) => info.GetCustomAttribute<NetworkSerialize>() ?? baseClass.GetType().GetCustomAttribute<NetworkSerialize>();
    }
}