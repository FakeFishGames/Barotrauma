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

        public ReliableMessage CreateMessage()
        {
            return sender.CreateMessage();
        }

        public void SendMessage(ReliableMessage message, NetConnection receiver)
        {
            try
            {
                sender.SendMessage(message, receiver);
            }
            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Sending a reliable message failed", e);
#endif
            }
        }

        public void HandleResendRequest(NetIncomingMessage inc)
        {
            sender.HandleResendRequest(inc);
        }

        public void HandleLatestMessageID(NetIncomingMessage inc)
        {
            //make sure we've received what's been sent to us, if not, rerequest
            receiver.HandleLatestMessageID(inc);
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
        private Dictionary<ushort, ReliableMessage> messageBuffer;

        private ushort messageCount;

        private NetPeer sender;

        private NetConnection recipient;

        private float idSendTimer;

        private float idSendInterval;

        public ReliableSender(NetPeer sender)
        {
            this.sender = sender;

            messageCount = ushort.MaxValue - 5;

            messageBuffer = new Dictionary<ushort, ReliableMessage>();
        }

        public ReliableMessage CreateMessage()
        {
            ushort messageID = (messageCount==ushort.MaxValue) ? (ushort)0 : (ushort)(messageCount + 1);

            NetOutgoingMessage message = sender.CreateMessage();

            var reliableMessage = new ReliableMessage(message, messageID);

            message.Write((byte)PacketTypes.ReliableMessage);

            message.Write(messageID);
            
            if (messageBuffer.Count > NetConfig.ReliableMessageBufferSize)
            {

                int end = messageCount - NetConfig.ReliableMessageBufferSize;
                int start = end - (messageBuffer.Count - NetConfig.ReliableMessageBufferSize);

                if (start<0)
                {
                    int wrappedStart = start + ushort.MaxValue;
                    if (wrappedStart==0) wrappedStart = ushort.MaxValue;
                    int wrappedEnd = end + ushort.MaxValue;
                    if (wrappedEnd==0) wrappedEnd = ushort.MaxValue;

                    for (ushort i = (ushort)wrappedStart; i <= (ushort)wrappedEnd; i++)
                    {
                        messageBuffer.Remove(i);
                        if (i == ushort.MaxValue) break;
                        Debug.WriteLine("removing message " + i);
                    }
                }

                for (ushort i = (ushort)Math.Max(start,0); i <= (ushort)Math.Max(end,0); i++)
                {
                    messageBuffer.Remove(i);
                    if (i == ushort.MaxValue) break;
                    Debug.WriteLine("removing message " + i);
                }  
            }

            return reliableMessage;

            //server.SendMessage(msg, server.Connections, NetDeliveryMethod.Unreliable, 0);
        }

        public void SendMessage(ReliableMessage message, NetConnection connection)
        {
            idSendInterval = 0.0f;
            idSendTimer = connection.AverageRoundtripTime;

            messageBuffer.Add(message.ID, message);

            if (messageCount == ushort.MaxValue) messageCount = 0;
            messageCount++;

            message.SaveInnerMessage();

            sender.SendMessage(message.InnerMessage, connection, NetDeliveryMethod.Unreliable, 0);

            recipient = connection;
        }

        public void HandleResendRequest(NetIncomingMessage inc)
        {
            ushort messageId = inc.ReadUInt16();

            Debug.WriteLine("received resend request for msg id "+messageId);

            ResendMessage(messageId, inc.SenderConnection);
        }

        private void ResendMessage(ushort messageId, NetConnection connection)
        {
            ReliableMessage message;
            if (!messageBuffer.TryGetValue(messageId, out message)) return;

            Debug.WriteLine("resending " + messageId);

            NetOutgoingMessage resendMessage = sender.CreateMessage();
            message.RestoreInnerMessage(resendMessage);

            idSendTimer = connection.AverageRoundtripTime;

            sender.SendMessage(resendMessage, connection, NetDeliveryMethod.Unreliable);
        }

        public void Update(float deltaTime)
        {
            if (recipient == null) return;

            idSendTimer -= deltaTime;

            if (idSendTimer > 0.0f) return;

            //Debug.WriteLine("Sending ack message: "+messageCount);

            NetOutgoingMessage message = sender.CreateMessage();
            message.Write((byte)PacketTypes.LatestMessageID);

            message.Write(messageCount);

            sender.SendMessage(message, recipient, NetDeliveryMethod.Unreliable);

            float roundTripTime = Math.Min(recipient.AverageRoundtripTime, 0.5f);

            idSendTimer = Math.Max(roundTripTime, NetConfig.IdSendInterval+idSendInterval);
            idSendInterval += 0.1f;
        }
    }

    internal class ReliableReceiver
    {
        ushort lastMessageID;

        Queue<ushort> missingMessageIds;
        Dictionary<ushort, MissingMessage> missingMessages;

        private NetPeer receiver;

        private NetConnection recipient;

        public ReliableReceiver(NetPeer receiver)
        {
            this.receiver = receiver;

            lastMessageID  = ushort.MaxValue - 5;
            
            missingMessages = new Dictionary<ushort,MissingMessage>();
            missingMessageIds = new Queue<ushort>();
        }

        public void Update(float deltaTime)
        {
            foreach (var message in missingMessages.Where(m => m.Value.ResendRequestsSent > NetConfig.ResendAttempts).ToList())
            {
                Debug.WriteLine("Max rerequest attempts reached on message "+message.Value.ID);
                missingMessages.Remove(message.Key);
            }

            while (missingMessageIds.Count>NetConfig.ReliableMessageBufferSize)
            {
                ushort id = missingMessageIds.Dequeue();

                missingMessages.Remove(id);
            }

            foreach (KeyValuePair<ushort, MissingMessage> valuePair in missingMessages)
            {
                MissingMessage missingMessage = valuePair.Value;

                missingMessage.ResendTimer -= deltaTime;

                if (missingMessage.ResendTimer > 0.0f) continue;
                
                Debug.WriteLine("rerequest "+missingMessage.ID+" (try #"+missingMessage.ResendRequestsSent+")");

                NetOutgoingMessage resendRequest = receiver.CreateMessage();
                resendRequest.Write((byte)PacketTypes.ResendRequest);

                resendRequest.Write(missingMessage.ID);

                receiver.SendMessage(resendRequest, recipient, 
                    missingMessage.ResendRequestsSent==0 ? NetDeliveryMethod.ReliableUnordered : NetDeliveryMethod.Unreliable);

                float roundTripTime = Math.Min(recipient.AverageRoundtripTime, 0.5f);

                missingMessage.ResendTimer = Math.Max(roundTripTime, NetConfig.RerequestInterval);
                missingMessage.ResendRequestsSent++;
                
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
                if (lastMessageID>ushort.MaxValue/2 && id>=1)
                {
                    for (ushort i = (ushort)(Math.Min(lastMessageID, (ushort)(ushort.MaxValue-1)) + 1); i < ushort.MaxValue; i++)
                    {
                        QueueMissingMessage(i);
                    }
                    for (ushort i = 1; i < id; i++)
                    {
                        QueueMissingMessage(i);
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
                    RemoveMissingMessage(id);
                }
            }
            else
            {
                if (id>lastMessageID+1)
                {                
                    for (ushort i = (ushort)(lastMessageID+1); i < id; i++ )
                    {
                        QueueMissingMessage(i); 
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
                    RemoveMissingMessage(id);
                }

                lastMessageID = Math.Max(lastMessageID, id);
            }

            return true;
        }

        private void QueueMissingMessage(ushort id)
        {
            //message already marked as missed, continue
            if (missingMessages.ContainsKey(id)) return;

            Debug.WriteLine("added " + id + " to missed");
            missingMessages.Add(id, new MissingMessage(id));

            missingMessageIds.Enqueue(id);                    
        }

        private void RemoveMissingMessage(ushort id)
        {
            if (!missingMessages.ContainsKey(id)) return;
            
            Debug.WriteLine("remove " + id + " from missed");
            missingMessages.Remove(id);            
        }

        public void HandleLatestMessageID(NetIncomingMessage inc)
        {
            ushort messageId = inc.ReadUInt16();

            recipient = inc.SenderConnection;

            //id matches, all good
            if (messageId == lastMessageID)
            {
                //Debug.WriteLine("Received ack message: " + messageId + ", all good");
                return;
            }

            if (lastMessageID > messageId && Math.Abs((int)lastMessageID - (int)messageId) < ushort.MaxValue / 2)
            {
                //shouldn't happen: we have somehow received messages that the other end hasn't sent
                Debug.WriteLine("Reliable message error - recipient last sent: " + messageId + " (current count " + lastMessageID + ")");
                return;
            }

            Debug.WriteLine("Received id update message: " + messageId + ", need to rerequest messages (last id: "+lastMessageID+")");

            if (lastMessageID > ushort.MaxValue / 2 && messageId < short.MaxValue / 2)
            {
                for (ushort i = (ushort)Math.Min((int)lastMessageID + 1, ushort.MaxValue); i <= ushort.MaxValue; i++)
                {
                    if (i == ushort.MaxValue && lastMessageID == ushort.MaxValue) break;
                    QueueMissingMessage(i);
                    if (i == ushort.MaxValue) break;
                }

                for (ushort i = 1; i < messageId; i++)
                {
                    QueueMissingMessage(i);
                }
            }
            else
            {
                //we already wrapped around but message hasn't, so it's an old message
                if (lastMessageID < ushort.MaxValue / 2 && messageId > ushort.MaxValue / 2)
                {
                    Debug.WriteLine("old already received message, ignore");
                    return;
                }

                for (ushort i = (ushort)Math.Min((int)lastMessageID+1, ushort.MaxValue); i <= messageId; i++)
                {
                    QueueMissingMessage(i);
                    if (i == ushort.MaxValue) break;
                }
            }
            
            lastMessageID = messageId;
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
            innerMessage.WritePadBits();
            innerMessageBytes = innerMessage.PeekBytes(innerMessage.LengthBytes);
            //innerMessage = null;
        }

        public void RestoreInnerMessage(NetOutgoingMessage message)
        {
            message.Write(innerMessageBytes);
        }            
    }
}
