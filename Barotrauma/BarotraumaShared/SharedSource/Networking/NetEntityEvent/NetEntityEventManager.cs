using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    public class EntityEventException : Exception
    {
        public readonly Entity Entity;

        public EntityEventException(string errorMessage, Entity causingEntity, Exception innerException = null) : base(errorMessage, innerException)
        {
            Entity = causingEntity;
        }
    }

    public abstract class NetEntityEventManager
    {
        public const int MaxEventBufferLength = 1024;

        /// <summary>
        /// Default hash to mark that the event is not associated with any plugin content package.
        /// There might be a very low chance that a plugin content package hash is the same as this value.
        /// However, it's so unlikely to happen that there are lengthy discussions on the internet about if it's
        /// even possible since not even an empty file will result in this hash.
        /// And the nature of hashes prevents finding concrete proof about it since they are designed to be irreversible
        /// and have mitigations against brute force attacks.
        /// <br/>
        /// Even if we are truncating the 128-bit MD5 hash to 32 bits,
        /// the chance of a collision is still astronomically low.
        /// A quick look on the internet shows that the lowest MD5 hash that I could find
        /// was found in 2016 by Jiyong Youn and has 14 leading zeros,
        /// which is still very far from 32 leading zeros.
        /// https://web.archive.org/web/20171201071711/http://www.crysys.hu/hashgame/allrecord.php
        /// <br/>
        /// Besides, hash collisions is something that high security researchers and cryptographers
        /// should worry about. Not game developers. :D
        /// </summary>
        public const int VanillaEventHash = 0;

        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(IWriteMessage msg, List<NetEntityEvent> eventsToSync, out List<NetEntityEvent> sentEvents, Client recipient = null)
        {
            //write into a temporary buffer so we can write the number of events before the actual data
            IWriteMessage tempBuffer = new WriteOnlyMessage();

            sentEvents = new List<NetEntityEvent>();

            int eventCount = 0;
            foreach (NetEntityEvent e in eventsToSync)
            {
                //write into a temporary buffer so we can write the length before the actual data
                IWriteMessage tempEventBuffer = new WriteOnlyMessage();
                try
                {
                    if (e.PluginContentPackageHash.TryUnwrap(out uint hash))
                    {
                        PluginData.WriteEvent(hash, tempEventBuffer, e, recipient);
                    }
                    else
                    {
                        WriteEvent(tempEventBuffer, e, recipient);
                    }
                }
                catch (Exception exception)
                {
                    DebugConsole.ThrowError($"Failed to write an event (ID: {e.ID}) for the entity \"{e.Entity}\"", exception, contentPackage: e.Entity?.ContentPackage);
                    GameAnalyticsManager.AddErrorEventOnce("NetEntityEventManager.Write:WriteFailed" + e.Entity.ToString(),
                        GameAnalyticsManager.ErrorSeverity.Error,
                        "Failed to write an event for the entity \"" + e.Entity + "\"\n" + exception.StackTrace.CleanupStackTrace());

                    //write an empty event to avoid messing up IDs
                    //(otherwise the clients might read the next event in the message and think its ID 
                    //is consecutive to the previous one, even though we skipped over this broken event)
                    tempBuffer.WriteUInt16(Entity.NullEntityID);
                    tempBuffer.WriteUInt32(VanillaEventHash);
                    tempBuffer.WriteVariableUInt32(0); //size of the event
                    eventCount++;
                    continue;
                }

                if (eventCount > 0 &&
                    msg.LengthBytes + tempBuffer.LengthBytes + tempEventBuffer.LengthBytes > MaxEventBufferLength)
                {
                    //no more room in this packet
                    break;
                }

                tempBuffer.WriteUInt16(e.EntityID);
                tempBuffer.WriteUInt32(e.PluginContentPackageHash.Fallback(VanillaEventHash));
                tempBuffer.WriteVariableUInt32((uint)tempEventBuffer.LengthBytes);
                tempBuffer.WriteBytes(tempEventBuffer.Buffer, 0, tempEventBuffer.LengthBytes);
                sentEvents.Add(e);

                eventCount++;
            }

            if (eventCount > 0)
            {
                msg.WritePadBits();
                msg.WriteUInt16(eventsToSync[0].ID);
                msg.WriteByte((byte)eventCount);
                msg.WriteBytes(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            }
        }

        protected static bool ValidateEntity(INetSerializable entity)
        {
            void error(string reason)
                => DebugConsole.ThrowError($"Can't create an entity event for {entity} - {reason}.\n{Environment.StackTrace.CleanupStackTrace()}");

            if (entity is Entity { Removed: var removed, IdFreed: var idFreed })
            {
                if (removed)
                {
                    error("the entity has been removed");
                    return false;
                }
                if (idFreed)
                {
                    error("the ID of the entity has been freed");
                    return false;
                }
            }
            else
            {
                error($"input is not of type {nameof(Entity)}");
                return false;
            }
            return true;
        }

        protected abstract void WriteEvent(IWriteMessage buffer, NetEntityEvent entityEvent, Client recipient = null);
    }    
}
