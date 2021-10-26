using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
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

            int executedEffectCount = msg.ReadByte();
            for (int i = 0; i < executedEffectCount; i++)
            {
                int index1 = msg.ReadByte();
                int index2 = msg.ReadByte();
                var selectedEffect = statusEffects[index1][index2];
                item.ApplyStatusEffect(selectedEffect, selectedEffect.type, deltaTime: 1.0f, worldPosition: item.Position);
            }

            if (item.body != null)
            {
                item.body.FarseerBody.BodyType = BodyType.Kinematic;
            }
        }
    }
}
