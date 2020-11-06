using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
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
                ShowOnDisplay(newOutputValue);
                item.SendSignal(0, newOutputValue, "signal_out", null);
                item.CreateServerEvent(this);
            }
        }

        partial void ShowOnDisplay(string input)
        {
            messageHistory.Add(input);
            while (messageHistory.Count > MaxMessages)
            {
                messageHistory.RemoveAt(0);
            }
        }

        public void SyncHistory()
        {
            //split too long messages to multiple parts
            foreach (string str in messageHistory)
            {
                string msgToSend = str;
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
                        item.CreateServerEvent(this, new string[] { msgToSend });
                        msgToSend = msgToSend.Remove(0, tempMsg.Length);
                    }
                }
                if (!string.IsNullOrEmpty(msgToSend))
                {
                    item.CreateServerEvent(this, new string[] { msgToSend });
                }               
            }            
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            if (extraData.Length > 2 && extraData[2] is string str)
            {
                msg.Write(str);
            }
            else
            {
                msg.Write(OutputValue);
            }
        }
    }
}