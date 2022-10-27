#nullable enable

using System;
using System.Collections.Immutable;
using Barotrauma;
using Barotrauma.Networking;
using FluentAssertions;
using FsCheck;
using Microsoft.Xna.Framework;
using Xunit;

namespace TestProject
{
    // ReSharper disable UnusedMember.Local NotAccessedField.Local UnusedMember.Global
    public sealed class INetSerializableStructTests
    {
        private class CustomGenerators
        {
            // no null strings!!!
            public static Arbitrary<string> StringGeneratorOverride() => Arb.Default.String().Generator.Where(s => s != null).ToArbitrary();
        }

        public INetSerializableStructTests()
        {
            Arb.Register<TestProject.CustomGenerators>();
            Arb.Register<CustomGenerators>();
        }

        [Fact]
        public void TestBitField()
        {
            // 0-length bitfield test
            SerializeDeserializeBitField(Array.Empty<bool>());
            
            // Normal bitfield test
            Prop.ForAll<bool[]>(SerializeDeserializeBitField).VerboseCheckThrowOnFailure();
            
            // Large bitfield test
            Prop.ForAll(
                Arb.Generate<bool[]>().Resize(1000).Where(arr => arr.Length >= 800)
                    .ToArbitrary(),
                SerializeDeserializeBitField).VerboseCheckThrowOnFailure();
        }

        [Fact]
        public void TestRanged()
        {
            Prop.ForAll(
                Arb.Generate<int>().Where(i => i <= 100 && i >= -100).ToArbitrary(),
                Arb.Generate<float>().Where(f => f <= 100f && f >= -100f).ToArbitrary(),
                SerializeDeserializeRanged).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestOptional()
        {
            Prop.ForAll<Option<Int32>>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<Option<Byte[]>>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<Option<String>>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestNested()
        {
            Prop.ForAll<String, Int32, Boolean>((arg1, arg2, arg3) => SerializeDeserializeNullableTuple(arg1, new TupleNullableStruct<int, bool> { One = arg2, Two = arg3 })).QuickCheckThrowOnFailure();
            Prop.ForAll<Byte, UInt64>((arg1, arg2) => SerializeDeserialize(new TupleNullableStruct<byte, ulong> { One = arg1, Two = arg2 })).QuickCheckThrowOnFailure();
            Prop.ForAll<Int16, String>((arg1, arg2) => SerializeDeserialize(new TupleNullableStruct<short, string> { One = arg1, Two = arg2 })).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestVector2()
        {
            Prop.ForAll<Vector2>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestColor()
        {
            Prop.ForAll<Color>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestEnum()
        {
            Prop.ForAll<EnumTest>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }
        
        [Fact]
        public void TestEnumFlags()
        {
            Prop.ForAll<EnumFlagsTest>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestArray()
        {
            Prop.ForAll<Int32[]>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<Boolean[]>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<String[]>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestImmutableArray()
        {
            Prop.ForAll<ImmutableArray<Int32>>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<ImmutableArray<Boolean>>(SerializeDeserialize).QuickCheckThrowOnFailure();
            Prop.ForAll<ImmutableArray<String>>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestNullable()
        {
            Prop.ForAll<Int32?, String?>(SerializeDeserializeNullableTuple).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestBoolean()
        {
            Prop.ForAll<Boolean>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestByte()
        {
            Prop.ForAll<Byte>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestUInt16()
        {
            Prop.ForAll<UInt16>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestInt16()
        {
            Prop.ForAll<Int16>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestUInt32()
        {
            Prop.ForAll<UInt32>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestInt32()
        {
            Prop.ForAll<Int32>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestUInt64()
        {
            Prop.ForAll<UInt64>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestInt64()
        {
            Prop.ForAll<Int64>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestSingle()
        {
            Prop.ForAll<Single>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestDouble()
        {
            Prop.ForAll<Double>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void TestString()
        {
            Prop.ForAll<String>(SerializeDeserialize).QuickCheckThrowOnFailure();
        }

        private enum EnumTest
        {
            One = 1,
            Two = 2,
            Three = 3,
            Thousand = 1000
        }

        [Flags]
        private enum EnumFlagsTest
        {
            None = 0,
            Bit0 = 1 << 0,
            Bit1 = 1 << 1,
            Bit2 = 1 << 2,
            Bit3 = 1 << 3
        }

        private struct TestRangedStruct : INetSerializableStruct
        {
            [NetworkSerialize(MinValueInt = -100, MaxValueInt = 100)]
            public int IntValue;

            [NetworkSerialize(MinValueFloat = -100, MaxValueFloat = 100, NumberOfBits = 16)]
            public float FloatValue;
        }

#pragma warning disable CS0649
        private struct TestStruct<T> : INetSerializableStruct
        {
            [NetworkSerialize]
            public T Value;

            public T NotSerializedValue;

            public T NotSerializedFunction() => throw new NotImplementedException();
        }

        private struct TupleNullableStruct<T, U> : INetSerializableStruct
        {
            [NetworkSerialize]
            public T? One;

            [NetworkSerialize]
            public U? Two;

            public (T, U) NotSerializedValue;
            public (T, U) NotSerializedFunction() => throw new NotImplementedException();
        }
#pragma warning restore CS0649

        private static void SerializeDeserializeRanged(int intValue, float floatValue)
        {
            ReadWriteMessage msg = new ReadWriteMessage();
            TestRangedStruct writeStruct = new TestRangedStruct
            {
                IntValue = intValue,
                FloatValue = floatValue
            };

            msg.WriteNetSerializableStruct(writeStruct);
            msg.BitPosition = 0;

            TestRangedStruct readStruct = INetSerializableStruct.Read<TestRangedStruct>(msg);

            readStruct.FloatValue.Should().BeApproximately(floatValue, 0.25f); // should be enough precision
            readStruct.IntValue.Should().Be(intValue);
        }

        private static void SerializeDeserialize<T>(T arg) where T : notnull
        {
            ReadWriteMessage msg = new ReadWriteMessage();
            TestStruct<T> writeStruct = new TestStruct<T>
            {
                Value = arg
            };

            msg.WriteNetSerializableStruct(writeStruct);
            msg.BitPosition = 0;

            TestStruct<T> readStruct = INetSerializableStruct.Read<TestStruct<T>>(msg);

            readStruct.Should().BeEquivalentTo(writeStruct, options => options
                .ComparingByMembers<TestStruct<T>>()
                .ComparingByMembers(typeof(Option<>)));
        }

        private static void SerializeDeserializeNullableTuple<T, U>(T arg1, U arg2)
        {
            ReadWriteMessage msg = new ReadWriteMessage();
            TupleNullableStruct<T, U> writeStruct = new TupleNullableStruct<T, U>
            {
                One = arg1,
                Two = arg2
            };

            msg.WriteNetSerializableStruct(writeStruct);
            msg.BitPosition = 0;

            TupleNullableStruct<T, U> readStruct = INetSerializableStruct.Read<TupleNullableStruct<T, U>>(msg);

            readStruct.Should().BeEquivalentTo(writeStruct, options => options
                .ComparingByMembers<TupleNullableStruct<T, U>>()
                .ComparingByMembers(typeof(Option<>)));
        }

        private static void SerializeDeserializeBitField(bool[] arg)
        {
            ReadWriteMessage msg = new ReadWriteMessage();
            WriteOnlyBitField bitFieldWrite = new WriteOnlyBitField();

            foreach (bool b in arg)
            {
                bitFieldWrite.WriteBoolean(b);
            }

            bitFieldWrite.WriteToMessage(msg);
            msg.BitPosition = 0;

            ReadOnlyBitField bitFieldRead = new ReadOnlyBitField(msg);

            foreach (bool b in arg)
            {
                bitFieldRead.ReadBoolean().Should().Be(b);
            }
        }
    }
}