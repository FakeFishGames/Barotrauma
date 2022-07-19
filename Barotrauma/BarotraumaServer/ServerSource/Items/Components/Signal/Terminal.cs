using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        private readonly struct ServerEventData : IEventData
        {
            public readonly int MsgIndex;
            public readonly string MsgToSend;
            
            public ServerEventData(int msgIndex, string msgToSend)
            {
                MsgIndex = msgIndex;
                MsgToSend = msgToSend;
            }
        }
        
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            string newOutputValue = msg.ReadString();

            if (item.CanClientAccess(c))
            {
                if (newOutputValue.Length > MaxMessageLength)
                {
                    newOutputValue = newOutputValue.Substring(0, MaxMessageLength);
                }
                GameServer.Log(GameServer.CharacterLogName(c.Character) + " entered \"" + newOutputValue + "\" on " + item.Name,
                    ServerLog.MessageType.ItemInteraction);
                OutputValue = newOutputValue;
                ShowOnDisplay(newOutputValue, addToHistory: true, TextColor);
                item.SendSignal(newOutputValue, "signal_out");
                item.CreateServerEvent(this);
            }
        }

        partial void ShowOnDisplay(string input, bool addToHistory, Color color)
        {
            if (addToHistory)
            {
                messageHistory.Add(new TerminalMessage(input, color));
                while (messageHistory.Count > MaxMessages)
                {
                    messageHistory.RemoveAt(0);
                }
            }
        }

        public void SyncHistory()
        {
            //split too long messages to multiple parts
            int msgIndex = 0;
            foreach (var (str, _) in messageHistory)
            {
                string msgToSend = str;
                if (string.IsNullOrEmpty(msgToSend))
                {
                    item.CreateServerEvent(this, new ServerEventData(msgIndex, msgToSend));
                    msgIndex++;
                    continue;
                }
                if (msgToSend.Length > MaxMessageLength)
                {
                    List<string> splitMessage = msgToSend.Split(' ').ToList();
                    for (int i = 0; i < splitMessage.Count; i++)
                    {
                        if (splitMessage[i].Length > MaxMessageLength)
                        {
                            string temp = splitMessage[i];
                            splitMessage[i] = temp.Substring(0, MaxMessageLength);
                            splitMessage.Insert(i + 1, temp.Substring(MaxMessageLength, temp.Length - MaxMessageLength));
                        }
                    }
                    while (msgToSend.Length > MaxMessageLength)
                    {
                        string tempMsg = "";
                        do
                        {
                            tempMsg += splitMessage[0];
                            splitMessage.RemoveAt(0);
                            if (!splitMessage.Any()) { break; }
                            tempMsg += " ";
                        } while (tempMsg.Length + splitMessage[0].Length < MaxMessageLength);
                        item.CreateServerEvent(this, new ServerEventData(msgIndex, tempMsg));
                        msgToSend = msgToSend.Remove(0, tempMsg.Length);
                    }
                }
                if (!string.IsNullOrEmpty(msgToSend))
                {
                    item.CreateServerEvent(this, new ServerEventData(msgIndex, msgToSend));
                }
                msgIndex++;
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (TryExtractEventData(extraData, out ServerEventData eventData))
            {
                msg.Write(eventData.MsgToSend);
            }
            else
            {
                msg.Write(OutputValue);
            }
        }
    }
}