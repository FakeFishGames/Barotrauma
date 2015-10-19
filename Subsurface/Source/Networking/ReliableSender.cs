using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking.ReliableMessages
{

    class ReliableChannel
    {
        ReliableSender sender;
        ReliableReceiver receiver;

        public ReliableChannel(NetPeer host)
        {
            sender = new ReliableSender(host);
            receiver = new ReliableReceiver(host);
        }

        public ReliableMessage CreateMessage(int lengthBytes = 0)
        {
            return sender.CreateMessage();
        }

        public void SendMessage(ReliableMessage message, NetConnection receiver)
        {
            sender.SendMessage(message, receiver);
        }

        public void HandleResendRequest(NetIncomingMessage inc)
        {
            sender.HandleResendRequest(inc);
        }

        public void HandleAckMessage(NetIncomingMessage inc)
        {
            //make sure we've received what's been sent to us, if not, rerequest
            receiver.HandleAckMessage(inc);
        }

        public bool CheckMessage(NetIncomingMessage inc)
        {
            return receiver.CheckMessage(inc);
        }

        public void Update(float deltaTime)
        {
            sender.Update(deltaTime);
            //update receiver to rerequest missed messages
            receiver.Update(deltaTime);
        }

    }

    internal class ReliableSender
    {
        private List<ReliableMessage> messageBuffer;

        private ushort messageCount;

        private NetPeer sender;

        private NetConnection recipient;

        private float ackTimer;

        public ReliableSender(NetPeer sender)
        {
            this.sender = sender;
            
            messageBuffer = new List<ReliableMessage>();
        }

        public ReliableMessage CreateMessage()
        {
            if (messageCount == ushort.MaxValue) messageCount = 0;
            messageCount++;

            NetOutgoingMessage message = sender.CreateMessage();

            var reliableMessage = new ReliableMessage(message, messageCount);
            messageBuffer.Add(reliableMessage);

            message.Write((byte)PacketTypes.ReliableMessage);
            message.Write(messageCount);
            
            while (messageBuffer.Count>100)
            {
                messageBuffer.RemoveAt(0);
            }

            return reliableMessage;

            //server.SendMessage(msg, server.Connections, NetDeliveryMethod.Unreliable, 0);
        }

        public void SendMessage(ReliableMessage message, NetConnection connection)
        {
            message.SaveInnerMessage();

            sender.SendMessage(message.InnerMessage, connection, NetDeliveryMethod.Unreliable, 0);

            recipient = connection;
        }

        //        NetOutgoingMessage msg = server.CreateMessage();
        //reliableSender.CreateMessage(msg);
        //msg.Write((byte)PacketTypes.Chatmessage);
        //msg.Write((byte)type);
        //msg.Write(message);



        public void HandleResendRequest(NetIncomingMessage inc)
        {
            ushort messageId = inc.ReadUInt16();

            Debug.WriteLine("received resend request for msg id "+messageId);

            ResendMessage(messageId, inc.SenderConnection);
        }

        private void ResendMessage(ushort messageId, NetConnection connection)
        {
            ReliableMessage message = messageBuffer.Find(m => m.ID == messageId);
            if (message == null) return;

            Debug.WriteLine("resending " + messageId);


            NetOutgoingMessage resendMessage = sender.CreateMessage();
            message.RestoreInnerMessage(resendMessage);

            sender.SendMessage(resendMessage, connection, NetDeliveryMethod.Unreliable);
        }

        public void Update(float deltaTime)
        {
            if (recipient == null) return;

            ackTimer -= deltaTime;

            if (ackTimer > 0.0f) return;

            Debug.WriteLine("Sending ack message: "+messageCount);

            NetOutgoingMessage message = sender.CreateMessage();
            message.Write((byte)PacketTypes.Ack);
            message.Write(messageCount);

            sender.SendMessage(message, recipient, NetDeliveryMethod.Unreliable);

            ackTimer = Math.Max(recipient.AverageRoundtripTime, 1.0f);
        }
    }

    internal class ReliableReceiver
    {
        ushort lastMessageID;

        Dictionary<ushort, MissingMessage> missingMessages;

        private NetPeer receiver;

        private NetConnection recipient;

        public ReliableReceiver(NetPeer receiver)
        {
            this.receiver = receiver;
            
            missingMessages = new Dictionary<ushort,MissingMessage>();
        }

        public void Update(float deltaTime)
        {
            foreach (var message in missingMessages.Where(m => m.Value.ResendRequestsSent>10).ToList())
            {
                missingMessages.Remove(message.Key);
            }

            foreach (KeyValuePair<ushort, MissingMessage> valuePair in missingMessages)
            {
                MissingMessage missingMessage = valuePair.Value;

                missingMessage.ResendTimer -= deltaTime;

                if (missingMessage.ResendRequestsSent==0
                    || missingMessage.ResendTimer<0.0f)
                {                    
                    Debug.WriteLine("rerequest "+missingMessage.ID+" (try #"+missingMessage.ResendRequestsSent+")");

                    NetOutgoingMessage resendRequest = receiver.CreateMessage();
                    resendRequest.Write((byte)PacketTypes.ResendRequest);
                    resendRequest.Write(missingMessage.ID);

                    receiver.SendMessage(resendRequest, recipient, NetDeliveryMethod.Unreliable);


                    missingMessage.ResendTimer = Math.Max(recipient.AverageRoundtripTime, 0.2f);
                    missingMessage.ResendRequestsSent++;
                }
            }

        }
        
        public bool CheckMessage(NetIncomingMessage message)
        {
            recipient = message.SenderConnection;

            ushort id = message.ReadUInt16();

            Debug.WriteLine("received message ID " + id + " - last id: " + lastMessageID);

            //wrapped around
            if (Math.Abs((int)lastMessageID - (int)id) > ushort.MaxValue / 2)
            {
                //id wrapped around and we missed some messages in between, rerequest them
                if (lastMessageID<=ushort.MaxValue && id>1)
                {
                    for (ushort i = (ushort)(Math.Min(lastMessageID, (ushort)(ushort.MaxValue-1)) + 1); i < ushort.MaxValue; i++)
                    {
                        //message already marked as missed, continue
                        if (missingMessages.ContainsKey((i))) continue;

                        Debug.WriteLine("added " + i + " to missed");
                        missingMessages.Add(i, new MissingMessage((ushort)i));
                    }
                    for (ushort i = 1; i < id; i++)
                    {
                        //message already marked as missed, continue
                        if (missingMessages.ContainsKey((i))) continue;

                        Debug.WriteLine("added " + i + " to missed");
                        missingMessages.Add(i, new MissingMessage((ushort)i));
                    }

                    lastMessageID = id;
                }
                //we already wrapped around but the message hasn't, check if it's a duplicate
                else if (lastMessageID < ushort.MaxValue / 2 && id > ushort.MaxValue / 2 && !missingMessages.ContainsKey(id))
                {
                    Debug.WriteLine("old already received message, ignore");
                    return false;
                }
                else
                {
                    if (missingMessages.ContainsKey(id))
                    {
                        Debug.WriteLine("remove " + id + " from missed");
                        missingMessages.Remove(id);
                    }
                }
            }
            else
            {
                if (id>lastMessageID+1)
                {                
                    for (ushort i = (ushort)(lastMessageID+1); i < id; i++ )
                    {
                        //message already marked as missed, continue
                        if (missingMessages.ContainsKey((i))) continue;

                        Debug.WriteLine("added "+i+" to missed");
                        missingMessages.Add(i, new MissingMessage((ushort)i));  
                    }

                }
                //received an old message and it wasn't marked as missed, lets ignore it
                else if (id<=lastMessageID && !missingMessages.ContainsKey(id))
                {
                    Debug.WriteLine("old already received message, ignore");
                    return false;
                }
                else
                {
                    if (missingMessages.ContainsKey(id))
                    {
                        Debug.WriteLine("remove "+id+" from missed");
                        missingMessages.Remove(id);
                    }
                }

                lastMessageID = Math.Max(lastMessageID, id);
            }

            return true;
        }

        public void HandleAckMessage(NetIncomingMessage inc)
        {
            int messageId = inc.ReadUInt16();

            recipient = inc.SenderConnection;

            //id matches, all good
            if (messageId == lastMessageID)
            {

                Debug.WriteLine("Received ack message: " + messageId + ", all good");
                return;
            }

            if (lastMessageID > messageId)
            {
                //shouldn't happen: we have somehow received messages that the other end hasn't sent
                Debug.WriteLine("Reliable message error - recipient last sent: " + messageId + " (current count " + lastMessageID + ")");
                return;
            }

            Debug.WriteLine("Received ack message: " + messageId + ", need to rerequest messages");

            if (lastMessageID > ushort.MaxValue / 2 && messageId < short.MaxValue / 2)
            {
                for (ushort i = (ushort)Math.Min((int)lastMessageID + 1, ushort.MaxValue); i <= ushort.MaxValue; i++)
                {

                    if (!missingMessages.ContainsKey(i)) missingMessages.Add(i, new MissingMessage(i));       
                }

                for (ushort i = 1; i <= messageId; i++)
                {
                    if (!missingMessages.ContainsKey(i)) missingMessages.Add(i, new MissingMessage(i));
                }
            }
            else
            {
                for (ushort i = (ushort)Math.Min((int)lastMessageID+1, ushort.MaxValue); i <= messageId; i++)
                {

                    if (!missingMessages.ContainsKey(i)) missingMessages.Add(i, new MissingMessage(i));  
                }
            }



            //    Debug.WriteLine("received recent request for msg id " + messageId);

            //ReliableMessage message = messageBuffer.Find(m => m.ID == messageId);
            //if (message == null) return;

            //NetOutgoingMessage resendMessage = sender.CreateMessage();
            //message.RestoreInnerMessage(resendMessage);

            //sender.SendMessage(resendMessage, inc.SenderConnection, NetDeliveryMethod.Unreliable);
        }
    }

    internal class MissingMessage
    {
        private ushort id;

        public byte ResendRequestsSent;

        public float ResendTimer;

        public ushort ID
        {
            get { return id; }
        }
        
        public MissingMessage(ushort id)
        {
            this.id = id;
        }
    }
    
    class ReliableMessage
    {
        private NetOutgoingMessage innerMessage;
        private ushort id;

        private byte[] innerMessageBytes;

        public NetOutgoingMessage InnerMessage
        {
            get { return innerMessage; }
        }

        public ushort ID
        {
            get { return id; }
        }


        public ReliableMessage(NetOutgoingMessage message, ushort id)
        {
            this.innerMessage = message;
            this.id = id;
        }

        public void SaveInnerMessage()
        {
            innerMessageBytes = innerMessage.PeekBytes(innerMessage.LengthBytes);
            //innerMessage = null;
        }

        public void RestoreInnerMessage(NetOutgoingMessage message)
        {
            message.Write(innerMessageBytes);
        }            
    }
}
