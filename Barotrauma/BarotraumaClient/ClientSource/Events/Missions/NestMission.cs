using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            nestPosition = new Vector2(
                msg.ReadSingle(),
                msg.ReadSingle());
            ushort itemCount = msg.ReadUInt16();
            for (int i = 0; i < itemCount; i++)
            {
                var item = Item.ReadSpawnData(msg);
                items.Add(item);
                if (item.body != null)
                {
                    item.body.FarseerBody.BodyType = BodyType.Kinematic;
                }
            }
        }
    }
}
