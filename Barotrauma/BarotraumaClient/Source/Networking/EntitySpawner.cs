using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void ClientRead(ServerNetObject type, IReadMessage message, float sendingTime)
        {
            bool remove = message.ReadBoolean();

            if (remove)
            {
                ushort entityId = message.ReadUInt16();

                var entity = FindEntityByID(entityId);
                if (entity != null)
                {
                    DebugConsole.Log("Received entity removal message for \"" + entity.ToString() + "\".");
                    entity.Remove();
                }
                else
                {
                    DebugConsole.Log("Received entity removal message for ID " + entityId + ". Entity with a matching ID not found.");
                }
            }
            else
            {
                switch (message.ReadByte())
                {
                    case (byte)SpawnableType.Item:
                        Item.ReadSpawnData(message, true);
                        break;
                    case (byte)SpawnableType.Character:
                        Character.ReadSpawnData(message, true);
                        break;
                    default:
                        DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                        break;
                }
            }
        }
    }
}
