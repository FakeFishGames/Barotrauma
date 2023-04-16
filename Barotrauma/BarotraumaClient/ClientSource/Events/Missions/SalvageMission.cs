using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => false;

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);

            foreach (var target in targets)
            {
                bool targetFound = msg.ReadBoolean();
                if (!targetFound) { continue; }

                bool usedExistingItem = msg.ReadBoolean();
                if (usedExistingItem)
                {
                    ushort id = msg.ReadUInt16();
                    target.Item = Entity.FindEntityByID(id) as Item;
                    if (target.Item == null)
                    {
                        throw new System.Exception("Error in SalvageMission.ClientReadInitial: failed to find item " + id + " (mission: " + Prefab.Identifier + ")");
                    }
                }
                else
                {
                    target.Item = Item.ReadSpawnData(msg);
                    if (target.Item == null)
                    {
                        throw new System.Exception("Error in SalvageMission.ClientReadInitial: spawned item was null (mission: " + Prefab.Identifier + ")");
                    }
                }

                int executedEffectCount = msg.ReadByte();
                for (int i = 0; i < executedEffectCount; i++)
                {
                    int listIndex = msg.ReadByte();
                    int effectIndex = msg.ReadByte();
                    var selectedEffect = target.StatusEffects[listIndex][effectIndex];
                    target.Item.ApplyStatusEffect(selectedEffect, selectedEffect.type, deltaTime: 1.0f, worldPosition: target.Item.Position);
                }

                if (target.Item.body != null)
                {
                    target.Item.body.FarseerBody.BodyType = BodyType.Kinematic;
                }
            }
        }

        public override void ClientRead(IReadMessage msg)
        {
            base.ClientRead(msg);
            int targetCount = msg.ReadByte();
            for (int i = 0; i < targetCount; i++)
            {
                var state = (Target.RetrievalState)msg.ReadByte();
                if (i < targets.Count)
                {
                    targets[i].State = state;
                }
            }
        }
    }
}
