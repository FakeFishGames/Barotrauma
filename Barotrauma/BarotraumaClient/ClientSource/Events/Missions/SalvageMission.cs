using Barotrauma.Networking;
using FarseerPhysics;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            item = Item.ReadSpawnData(msg);
            if (item == null)
            {
                throw new System.Exception("Error in SalvageMission.ClientReadInitial: spawned item was null (mission: " + Prefab.Identifier + ")");
            }

            item.body.FarseerBody.BodyType = BodyType.Kinematic;
        }
    }
}
