using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            bool usedExistingItem = msg.ReadBoolean();
            if (usedExistingItem)
            {
                ushort id = msg.ReadUInt16();
                item = Entity.FindEntityByID(id) as Item;
                if (item == null)
                {
                    throw new System.Exception("Error in SalvageMission.ClientReadInitial: failed to find item " + id + " (mission: " + Prefab.Identifier + ")");
                }
            }
            else
            {
                item = Item.ReadSpawnData(msg);
                if (item == null)
                {
                    throw new System.Exception("Error in SalvageMission.ClientReadInitial: spawned item was null (mission: " + Prefab.Identifier + ")");
                }
            }

            item.body.FarseerBody.BodyType = BodyType.Kinematic;
        }
    }
}
