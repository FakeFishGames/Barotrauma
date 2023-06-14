#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public sealed class NetworkSerialize : Attribute
    {
        public int MaxValueInt = int.MaxValue;
        public int MinValueInt = int.MinValue;
        public float MaxValueFloat = float.MaxValue;
        public float MinValueFloat = float.MinValue;
        public int NumberOfBits = 8;
        public bool IncludeColorAlpha = false;
        public int ArrayMaxSize = ushort.MaxValue;

        public readonly int OrderKey;

        public NetworkSerialize([CallerLineNumber] int lineNumber = 0)
        {
            OrderKey = lineNumber;
        }
    }

    /// <summary>
    /// Static class that contains serialize and deserialize functions for different types used in <see cref="INetSerializableStruct"/>
    /// </summary>
    [SuppressMessage("ReSharper", "RedundantTypeArgumentsOfMethod")]
    static class NetSerializableProperties
    {
        public interface IReadWriteBehavior
        {
            public delegate object? ReadDelegate(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField);

            public delegate void WriteDelegate(object? obj, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField);

            public ReadDelegate ReadAction { get; }
            public WriteDelegate WriteAction { get; }
        }

        public readonly struct ReadWriteBehavior<T> : IReadWriteBehavior
        {
            public delegate T ReadDelegate(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField);

            public delegate void WriteDelegate(T obj, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField);

            public IReadWriteBehavior.ReadDelegate ReadAction { get; }
            public IReadWriteBehavior.WriteDelegate WriteAction { get; }

            public ReadDelegate ReadActionDirect { get; }
            public WriteDelegate WriteActionDirect { get; }

            public ReadWriteBehavior(ReadDelegate readAction, WriteDelegate writeAction)
            {
                ReadAction = (inc, attribute, bitField) => readAction(inc, attribute, bitField);
                WriteAction = (o, attribute, msg, bitField) => writeAction((T)o!, attribute, msg, bitField);
                ReadActionDirect = readAction;
                WriteActionDirect = writeAction;
            }
        }

        public readonly struct CachedReflectedVariable
        {
            public delegate object? GetValueDelegate(object? obj);

            public delegate void SetValueDelegate(object? obj, object? value);

            public readonly string Name;
            public readonly Type Type;
            public readonly IReadWriteBehavior Behavior;
            public readonly NetworkSerialize Attribute;
            public readonly SetValueDelegate SetValue;
            public readonly GetValueDelegate GetValue;
            public readonly bool HasOwnAttribute;

            public CachedReflectedVariable(MemberInfo info, IReadWriteBehavior behavior, Type baseClassType)
            {
                Behavior = behavior;
                Name = info.Name;
                switch (info)
                {
                    case PropertyInfo pi:
                        Type = pi.PropertyType;
                        GetValue = pi.GetValue;
                        SetValue = pi.SetValue;
                        break;
                    case FieldInfo fi:
                        Type = fi.FieldType;
                        GetValue = fi.GetValue;
                        SetValue = fi.SetValue;
                        break;
                    default:
                        throw new ArgumentException($"Expected {nameof(FieldInfo)} or {nameof(PropertyInfo)} but found {info.GetType()}.", nameof(info));
                }

                if (info.GetCustomAttribute<NetworkSerialize>() is { } ownAttriute)
                {
                    HasOwnAttribute = true;
                    Attribute = ownAttriute;
                }
                else if (baseClassType.GetCustomAttribute<NetworkSerialize>() is { } globalAttribute)
                {
                    HasOwnAttribute = false;
                    Attribute = globalAttribute;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to serialize \"{Type}\" in \"{baseClassType}\" because it has no {nameof(NetworkSerialize)} attribute.");
                }
            }
        }

        private static readonly Dictionary<Type, ImmutableArray<CachedReflectedVariable>> CachedVariables = new Dictionary<Type, ImmutableArray<CachedReflectedVariable>>();

        private static readonly Dictionary<Type, IReadWriteBehavior> TypeBehaviors
            = new Dictionary<Type, IReadWriteBehavior>
            {
                { typeof(Boolean), new ReadWriteBehavior<Boolean>(ReadBoolean, WriteBoolean) },
                { typeof(Byte), new ReadWriteBehavior<Byte>(ReadByte, WriteByte) },
                { typeof(UInt16), new ReadWriteBehavior<UInt16>(ReadUInt16, WriteUInt16) },
                { typeof(Int16), new ReadWriteBehavior<Int16>(ReadInt16, WriteInt16) },
                { typeof(UInt32), new ReadWriteBehavior<UInt32>(ReadUInt32, WriteUInt32) },
                { typeof(Int32), new ReadWriteBehavior<Int32>(ReadInt32, WriteInt32) },
                { typeof(UInt64), new ReadWriteBehavior<UInt64>(ReadUInt64, WriteUInt64) },
                { typeof(Int64), new ReadWriteBehavior<Int64>(ReadInt64, WriteInt64) },
                { typeof(Single), new ReadWriteBehavior<Single>(ReadSingle, WriteSingle) },
                { typeof(Double), new ReadWriteBehavior<Double>(ReadDouble, WriteDouble) },
                { typeof(String), new ReadWriteBehavior<String>(ReadString, WriteString) },
                { typeof(Identifier), new ReadWriteBehavior<Identifier>(ReadIdentifier, WriteIdentifier) },
                { typeof(AccountId), new ReadWriteBehavior<AccountId>(ReadAccountId, WriteAccountId) },
                { typeof(Color), new ReadWriteBehavior<Color>(ReadColor, WriteColor) },
                { typeof(Vector2), new ReadWriteBehavior<Vector2>(ReadVector2, WriteVector2) },
                { typeof(SerializableDateTime), new ReadWriteBehavior<SerializableDateTime>(ReadSerializableDateTime, WriteSerializableDateTime) }
            };

        private static readonly ImmutableDictionary<Predicate<Type>, Func<Type, IReadWriteBehavior>> BehaviorFactories = new Dictionary<Predicate<Type>, Func<Type, IReadWriteBehavior>>
        {
            // Arrays
            { type => type.IsArray, CreateArrayBehavior },

            // Nested INetSerializableStructs
            { type => typeof(INetSerializableStruct).IsAssignableFrom(type), CreateINetSerializableStructBehavior },

            // Enums
            { type => type.IsEnum, CreateEnumBehavior },

            // Nullable
            { type => Nullable.GetUnderlyingType(type) != null, CreateNullableStructBehavior },

            // ImmutableArray
            { type => IsOfGenericType(type, typeof(ImmutableArray<>)), CreateImmutableArrayBehavior },

            // Option
            { type => IsOfGenericType(type, typeof(Option<>)), CreateOptionBehavior }
        }.ToImmutableDictionary();

        /// <param name="behaviorGenericParam">The type that the behavior handles</param>
        /// <param name="funcGenericParam">The type that will be used as the generic parameter for the read/write methods</param>
        /// <param name="readFunc">The read method.
        /// It must have a generic parameter.
        /// The return type must be such that if the generic parameter is replaced with funcGenericParam, you get behaviorGenericParam.</param>
        /// <param name="writeFunc">The write method. The first parameter's type must be the same as readFunc's return type.</param>
        /// <typeparam name="TDelegateBase">Ideally the least specific type possible, because it's replaced by behaviorGenericParam</typeparam>
        /// <returns>A ReadWriteBehavior&lt;behaviorGenericParam&gt;</returns>
        private static IReadWriteBehavior CreateBehavior<TDelegateBase>(Type behaviorGenericParam,
                                                                        Type funcGenericParam,
                                                                        ReadWriteBehavior<TDelegateBase>.ReadDelegate readFunc,
                                                                        ReadWriteBehavior<TDelegateBase>.WriteDelegate writeFunc)
        {
            var behaviorType = typeof(ReadWriteBehavior<>).MakeGenericType(behaviorGenericParam);

            var readDelegateType = typeof(ReadWriteBehavior<>.ReadDelegate).MakeGenericType(behaviorGenericParam);
            var writeDelegateType = typeof(ReadWriteBehavior<>.WriteDelegate).MakeGenericType(behaviorGenericParam);

            var constructor = behaviorType.GetConstructor(new[]
            {
                readDelegateType, writeDelegateType
            });

            return (constructor!.Invoke(new object[]
            {
                readFunc.Method.GetGenericMethodDefinition().MakeGenericMethod(funcGenericParam).CreateDelegate(readDelegateType),
                writeFunc.Method.GetGenericMethodDefinition().MakeGenericMethod(funcGenericParam).CreateDelegate(writeDelegateType)
            }) as IReadWriteBehavior)!;
        }

        private static IReadWriteBehavior CreateArrayBehavior(Type arrayType) =>
            CreateBehavior(
                arrayType,
                arrayType.GetElementType()!,
                ReadArray<object>,
                WriteArray<object>);

        private static IReadWriteBehavior CreateINetSerializableStructBehavior(Type structType) =>
            CreateBehavior(
                structType,
                structType,
                ReadINetSerializableStruct<INetSerializableStruct>,
                WriteINetSerializableStruct<INetSerializableStruct>);

        private static IReadWriteBehavior CreateEnumBehavior(Type enumType) =>
            CreateBehavior(
                enumType,
                enumType,
                ReadEnum<Enum>,
                WriteEnum<Enum>);

        private static IReadWriteBehavior CreateNullableStructBehavior(Type nullableType) =>
            CreateBehavior(
                nullableType,
                Nullable.GetUnderlyingType(nullableType)!,
                ReadNullable<int>,
                WriteNullable<int>);

        private static IReadWriteBehavior CreateOptionBehavior(Type optionType) =>
            CreateBehavior(
                optionType,
                optionType.GetGenericArguments()[0],
                ReadOption<object>,
                WriteOption<object>);

        private static IReadWriteBehavior CreateImmutableArrayBehavior(Type arrayType) =>
            CreateBehavior(
                arrayType,
                arrayType.GetGenericArguments()[0],
                ReadImmutableArray<object>,
                WriteImmutableArray<object>);

        private static ImmutableArray<T> ReadImmutableArray<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : notnull
        {
            return ReadArray<T>(inc, attribute, bitField).ToImmutableArray();
        }

        private static void WriteImmutableArray<T>(ImmutableArray<T> array, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : notnull
        {
            ToolBox.ThrowIfNull(array);
            WriteIReadOnlyCollection<T>(array, attribute, msg, bitField);
        }

        private static T[] ReadArray<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : notnull
        {
            int length = bitField.ReadInteger(0, attribute.ArrayMaxSize);

            T[] array = new T[length];

            if (!TryFindBehavior(out ReadWriteBehavior<T> behavior))
            {
                throw new InvalidOperationException($"Could not find suitable behavior for type {typeof(T)} in {nameof(ReadArray)}");
            }

            for (int i = 0; i < length; i++)
            {
                array[i] = behavior.ReadActionDirect(inc, attribute, bitField);
            }

            return array;
        }

        private static void WriteArray<T>(T[] array, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : notnull
        {
            ToolBox.ThrowIfNull(array);
            WriteIReadOnlyCollection(array, attribute, msg, bitField);
        }

        private static void WriteIReadOnlyCollection<T>(IReadOnlyCollection<T> array, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : notnull
        {
            bitField.WriteInteger(array.Count, 0, attribute.ArrayMaxSize);

            if (!TryFindBehavior(out ReadWriteBehavior<T> behavior))
            {
                throw new InvalidOperationException($"Could not find suitable behavior for type {typeof(T)} in {nameof(WriteArray)}");
            }

            foreach (T o in array)
            {
                behavior.WriteActionDirect(o, attribute, msg, bitField);
            }
        }

        private static T ReadINetSerializableStruct<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : INetSerializableStruct
        {
            return INetSerializableStruct.ReadInternal<T>(inc, bitField);
        }

        private static void WriteINetSerializableStruct<T>(T serializableStruct, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : INetSerializableStruct
        {
            ToolBox.ThrowIfNull(serializableStruct);
            serializableStruct.WriteInternal(msg, bitField);
        }

        private static T ReadEnum<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : Enum
        {
            var type = typeof(T);

            Range<int> range = GetEnumRange(type);
            int enumIndex = bitField.ReadInteger(range.Start, range.End);

            if (typeof(T).GetCustomAttribute<FlagsAttribute>() != null)
            {
                return (T)(object)enumIndex;
            }
            
            foreach (T e in (T[])Enum.GetValues(type))
            {
                if (((int)(object)e) == enumIndex) { return e; }
            }

            throw new InvalidOperationException($"An enum {type} with value {enumIndex} could not be found in {nameof(ReadEnum)}");
        }

        private static void WriteEnum<T>(T value, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : Enum
        {
            ToolBox.ThrowIfNull(value);

            Range<int> range = GetEnumRange(typeof(T));
            bitField.WriteInteger((int)Convert.ChangeType(value, value.GetTypeCode()), range.Start, range.End);
        }

        private static T? ReadNullable<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : struct =>
            ReadOption<T>(inc, attribute, bitField).TryUnwrap(out var value) ? value : null;

        private static void WriteNullable<T>(T? value, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : struct =>
            WriteOption<T>(value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None(), attribute, msg, bitField);

        private static Option<T> ReadOption<T>(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) where T : notnull
        {
            bool hasValue = bitField.ReadBoolean();
            if (!hasValue)
            {
                return Option<T>.None();
            }

            if (TryFindBehavior(out ReadWriteBehavior<T> behavior))
            {
                return Option<T>.Some(behavior.ReadActionDirect(inc, attribute, bitField));
            }

            throw new InvalidOperationException($"Could not find suitable behavior for type {typeof(T)} in {nameof(ReadOption)}");
        }

        private static void WriteOption<T>(Option<T> option, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) where T : notnull
        {
            ToolBox.ThrowIfNull(option);

            if (option.TryUnwrap(out T? value))
            {
                bitField.WriteBoolean(true);
                if (TryFindBehavior(out ReadWriteBehavior<T> behavior))
                {
                    behavior.WriteActionDirect(value, attribute, msg, bitField);
                }
            }
            else
            {
                bitField.WriteBoolean(false);
            }
        }

        private static bool ReadBoolean(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => bitField.ReadBoolean();
        private static void WriteBoolean(bool b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { bitField.WriteBoolean(b); }
        
        private static byte ReadByte(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadByte();
        private static void WriteByte(byte b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteByte(b); }

        private static ushort ReadUInt16(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadUInt16();
        private static void WriteUInt16(ushort b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteUInt16(b); }

        private static short ReadInt16(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadInt16();
        private static void WriteInt16(short b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteInt16(b); }

        private static uint ReadUInt32(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadUInt32();
        private static void WriteUInt32(uint b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteUInt32(b); }

        private static int ReadInt32(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField)
        {
            if (IsRanged(attribute.MinValueInt, attribute.MaxValueInt))
            {
                return bitField.ReadInteger(attribute.MinValueInt, attribute.MaxValueInt);
            }

            return inc.ReadInt32();
        }

        private static void WriteInt32(int i, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            ToolBox.ThrowIfNull(i);

            if (IsRanged(attribute.MinValueInt, attribute.MaxValueInt))
            {
                bitField.WriteInteger(i, attribute.MinValueInt, attribute.MaxValueInt);
                return;
            }

            msg.WriteInt32(i);
        }

        private static ulong ReadUInt64(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadUInt64();
        private static void WriteUInt64(ulong b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteUInt64(b); }

        private static long ReadInt64(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadInt64();
        private static void WriteInt64(long b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteInt64(b); }

        private static float ReadSingle(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField)
        {
            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                return bitField.ReadFloat(attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
            }

            return inc.ReadSingle();
        }

        private static void WriteSingle(float f, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            ToolBox.ThrowIfNull(f);

            if (IsRanged(attribute.MinValueFloat, attribute.MaxValueFloat))
            {
                bitField.WriteFloat(f, attribute.MinValueFloat, attribute.MaxValueFloat, attribute.NumberOfBits);
                return;
            }

            msg.WriteSingle(f);
        }

        private static double ReadDouble(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadDouble();
        private static void WriteDouble(double b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteDouble(b); }

        private static string ReadString(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadString();
        private static void WriteString(string b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteString(b); }

        private static Identifier ReadIdentifier(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => inc.ReadIdentifier();
        private static void WriteIdentifier(Identifier b, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField) { msg.WriteIdentifier(b); }

        private static AccountId ReadAccountId(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField)
        {
            string str = inc.ReadString();
            return AccountId.Parse(str).TryUnwrap(out var accountId)
                ? accountId
                : throw new InvalidCastException($"Could not parse \"{str}\" as an {nameof(AccountId)}");
        }

        private static void WriteAccountId(AccountId accountId, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            msg.WriteString(accountId.StringRepresentation);
        }

        private static Color ReadColor(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField) => attribute.IncludeColorAlpha ? inc.ReadColorR8G8B8A8() : inc.ReadColorR8G8B8();

        private static void WriteColor(Color color, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            ToolBox.ThrowIfNull(color);

            if (attribute.IncludeColorAlpha)
            {
                msg.WriteColorR8G8B8A8(color);
                return;
            }

            msg.WriteColorR8G8B8(color);
        }

        private static Vector2 ReadVector2(IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField)
        {
            float x = ReadSingle(inc, attribute, bitField);
            float y = ReadSingle(inc, attribute, bitField);

            return new Vector2(x, y);
        }

        private static void WriteVector2(Vector2 vector2, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            ToolBox.ThrowIfNull(vector2);

            var (x, y) = vector2;
            WriteSingle(x, attribute, msg, bitField);
            WriteSingle(y, attribute, msg, bitField);
        }

        private static readonly Range<Int64> ValidTickRange
            = new Range<Int64>(
                start: DateTime.MinValue.Ticks,
                end: DateTime.MaxValue.Ticks);
        private static readonly Range<Int16> ValidTimeZoneMinuteRange
            = new Range<Int16>(
                start: (Int16)TimeSpan.FromHours(-12).TotalMinutes,
                end: (Int16)TimeSpan.FromHours(14).TotalMinutes);

        private static SerializableDateTime ReadSerializableDateTime(
            IReadMessage inc, NetworkSerialize attribute, ReadOnlyBitField bitField)
        {
            var ticks = inc.ReadInt64();
            var timezone = inc.ReadInt16();

            if (!ValidTickRange.Contains(ticks))
            {
                throw new Exception($"Incoming SerializableDateTime ticks out of range (ticks: {ticks}, timezone: {timezone})");
            }
            if (!ValidTimeZoneMinuteRange.Contains(timezone))
            {
                throw new Exception($"Incoming SerializableDateTime timezone out of range (ticks: {ticks}, timezone: {timezone})");
            }

            return new SerializableDateTime(new DateTime(ticks),
                new SerializableTimeZone(TimeSpan.FromMinutes(timezone)));
        }

        private static void WriteSerializableDateTime(
            SerializableDateTime dateTime, NetworkSerialize attribute, IWriteMessage msg, WriteOnlyBitField bitField)
        {
            msg.WriteInt64(dateTime.Ticks);
            msg.WriteInt16((Int16)(dateTime.TimeZone.Value.Ticks / TimeSpan.TicksPerMinute));
        }
        
        private static bool IsRanged(float minValue, float maxValue) => minValue > float.MinValue || maxValue < float.MaxValue;
        private static bool IsRanged(int minValue, int maxValue) => minValue > int.MinValue || maxValue < int.MaxValue;

        private static Range<int> GetEnumRange(Type type)
        {
            ImmutableArray<int> values = Enum.GetValues(type).Cast<int>().ToImmutableArray();
            return new Range<int>(values.Min(), values.Max());
        }

        private static bool TryFindBehavior<T>(out ReadWriteBehavior<T> behavior) where T : notnull
        {
            bool found = TryFindBehavior(typeof(T), out var bhvr);
            behavior = found ? (ReadWriteBehavior<T>)bhvr : default;
            return found;
        }

        private static bool TryFindBehavior(Type type, out IReadWriteBehavior behavior)
        {
            if (TypeBehaviors.TryGetValue(type, out var outBehavior))
            {
                behavior = outBehavior;
                return true;
            }

            foreach (var (predicate, factory) in BehaviorFactories)
            {
                if (!predicate(type)) { continue; }

                behavior = factory(type);
                TypeBehaviors.Add(type, behavior);
                return true;
            }

            behavior = default!;
            return false;
        }

        public static ImmutableArray<CachedReflectedVariable> GetPropertiesAndFields(Type type)
        {
            if (CachedVariables.TryGetValue(type, out var cached)) { return cached; }

            List<CachedReflectedVariable> variables = new List<CachedReflectedVariable>();

            IEnumerable<PropertyInfo> propertyInfos = type.GetProperties().Where(HasAttribute).Where(NotStatic);
            IEnumerable<FieldInfo> fieldInfos = type.GetFields().Where(HasAttribute).Where(NotStatic);

            foreach (PropertyInfo info in propertyInfos)
            {
                if (info.SetMethod is null)
                {
                    //skip get-only properties, because it's
                    //useful to have them but their value
                    //cannot be set when reading a struct
                    continue;
                }
                if (TryFindBehavior(info.PropertyType, out IReadWriteBehavior behavior))
                {
                    variables.Add(new CachedReflectedVariable(info, behavior, type));
                }
                else
                {
                    throw new Exception($"Unable to serialize type \"{type}\".");
                }
            }

            foreach (FieldInfo info in fieldInfos)
            {
                if (TryFindBehavior(info.FieldType, out IReadWriteBehavior behavior))
                {
                    variables.Add(new CachedReflectedVariable(info, behavior, type));
                }
                else
                {
                    throw new Exception($"Unable to serialize type \"{type}\".");
                }
            }

            ImmutableArray<CachedReflectedVariable> array = variables.All(v => v.HasOwnAttribute) ? variables.OrderBy(v => v.Attribute.OrderKey).ToImmutableArray() : variables.ToImmutableArray();
            CachedVariables.Add(type, array);
            return array;

            bool HasAttribute(MemberInfo info) => (info.GetCustomAttribute<NetworkSerialize>() ?? type.GetCustomAttribute<NetworkSerialize>()) != null;

            static bool NotStatic(MemberInfo info)
                => info switch
                {
                    PropertyInfo property => property.GetGetMethod() is { IsStatic: false },
                    FieldInfo field => !field.IsStatic,
                    _ => false
                };
        }

        private static bool IsOfGenericType(Type type, Type comparedTo)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == comparedTo;
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
    /// <see cref="Barotrauma.Networking.AccountId"/><br/>
    /// <see cref="System.Collections.Immutable.ImmutableArray{T}"></see><br/>
    /// <see cref="Microsoft.Xna.Framework.Color"/><br/>
    /// <see cref="Microsoft.Xna.Framework.Vector2"/><br/>
    /// In addition arrays, enums, <see cref="Nullable{T}"/> and <see cref="Option{T}"/> are supported.<br/>
    /// Using <see cref="Nullable{T}"/> or <see cref="Option{T}"/> will make the field or property optional.
    /// </remarks>
    /// <seealso cref="NetworkSerialize"/>
    internal interface INetSerializableStruct
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
        public static T Read<T>(IReadMessage inc) where T : INetSerializableStruct
        {
            ReadOnlyBitField bitField = new ReadOnlyBitField(inc);
            return ReadInternal<T>(inc, bitField);
        }

        public static T ReadInternal<T>(IReadMessage inc, ReadOnlyBitField bitField) where T : INetSerializableStruct
        {
            object? newObject = Activator.CreateInstance(typeof(T));
            if (newObject is null) { return default!; }

            var properties = NetSerializableProperties.GetPropertiesAndFields(typeof(T));
            foreach (NetSerializableProperties.CachedReflectedVariable property in properties)
            {
                object? value = property.Behavior.ReadAction(inc, property.Attribute, bitField);
                try
                {
                    property.SetValue(newObject, value);
                }
                catch (Exception exception)
                {
                    throw new Exception($"Failed to assign" +
                                        $" {value ?? "[NULL]"} ({value?.GetType().Name ?? "[NULL]"})" +
                                        $" to {typeof(T).Name}.{property.Name} ({property.Type.Name})", exception);
                }
            }

            return (T)newObject;
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
            WriteOnlyBitField bitField = new WriteOnlyBitField();
            IWriteMessage structWriteMsg = new WriteOnlyMessage();
            WriteInternal(structWriteMsg, bitField);
            bitField.WriteToMessage(msg);
            msg.WriteBytes(structWriteMsg.Buffer, 0, structWriteMsg.LengthBytes);
        }

        public void WriteInternal(IWriteMessage msg, WriteOnlyBitField bitField)
        {
            var properties = NetSerializableProperties.GetPropertiesAndFields(GetType());

            foreach (NetSerializableProperties.CachedReflectedVariable property in properties)
            {
                object? value = property.GetValue(this);
                property.Behavior.WriteAction(value!, property.Attribute, msg, bitField);
            }
        }
    }
}