using System;
using System.Runtime.CompilerServices;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    internal static class WriteOnlyMessageExtensions
    {
#if CLIENT
        public static IWriteMessage WithHeader(this IWriteMessage msg, ClientPacketHeader header)
        {
            msg.WriteByte((byte)header);
            return msg;
        }
#elif SERVER
        public static IWriteMessage WithHeader(this IWriteMessage msg, ServerPacketHeader header)
        {
            msg.WriteByte((byte)header);
            return msg;
        }
#endif
        public static void WriteNetSerializableStruct(this IWriteMessage msg, INetSerializableStruct serializableStruct)
        {
            serializableStruct.Write(msg);
        }

        public static NetOutgoingMessage ToLidgren(this IWriteMessage msg, NetPeer peer)
        {
            NetOutgoingMessage outMsg = peer.CreateMessage();
            outMsg.Write(msg.Buffer, 0, msg.LengthBytes);
            return outMsg;
        }
    }

    internal static class NetIncomingMessageExtensions
    {
        public static T ReadHeader<T>(this NetIncomingMessage msg) where T : Enum
        {
            byte header = msg.ReadByte();
            return Unsafe.As<byte, T>(ref header);
        }

        public static IReadMessage ToReadMessage(this NetIncomingMessage msg)
        {
            return new ReadWriteMessage(msg.Data, 0, msg.LengthBits, copyBuf: false);
        }
    }

    internal static class DeliveryMethodExtensions
    {
        public static NetDeliveryMethod ToLidgren(this DeliveryMethod deliveryMethod) =>
            deliveryMethod switch
            {
                DeliveryMethod.Unreliable => NetDeliveryMethod.Unreliable,
                DeliveryMethod.Reliable => NetDeliveryMethod.ReliableUnordered,
                DeliveryMethod.ReliableOrdered => NetDeliveryMethod.ReliableOrdered,
                _ => NetDeliveryMethod.Unreliable
            };

        public static Steamworks.P2PSend ToSteam(this DeliveryMethod deliveryMethod) =>
            deliveryMethod switch
            {
                DeliveryMethod.Reliable => Steamworks.P2PSend.Reliable,
                DeliveryMethod.ReliableOrdered => Steamworks.P2PSend.Unreliable,
                _ => Steamworks.P2PSend.Unreliable
            };
    }
}