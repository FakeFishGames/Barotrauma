using Lidgren.Network;
using System;

namespace Barotrauma.Networking
{
    partial class ChatMessage
    {
        public void ClientWrite(NetOutgoingMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write(Text);
        }

        public static void ClientRead(NetIncomingMessage msg)
        {
            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            string txt = msg.ReadString();

            string senderName = msg.ReadString();
            Character senderCharacter = null;
            bool hasSenderCharacter = msg.ReadBoolean();
            if (hasSenderCharacter)
            {
                senderCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                if (senderCharacter != null)
                {
                    senderName = senderCharacter.Name;
                }
            }

            if (NetIdUtils.IdMoreRecent(ID, LastID))
            {
                switch (type)
                {
                    case ChatMessageType.MessageBox:
                        new GUIMessageBox("", txt);
                        break;
                    case ChatMessageType.Console:
                        DebugConsole.NewMessage(txt, MessageColor[(int)ChatMessageType.Console]);
                        break;
                    case ChatMessageType.ServerLog:
                        if (!Enum.TryParse(senderName, out ServerLog.MessageType messageType))
                        {
                            return;
                        }
                        GameMain.Client.ServerLog?.WriteLine(txt, messageType);
                        break;
                    default:
                        GameMain.Client.AddChatMessage(txt, type, senderName, senderCharacter);
                        break;
                }
                
                LastID = ID;
            }
        }
    }
}
