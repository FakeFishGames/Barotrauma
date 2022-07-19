using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        public override bool DisplayAsCompleted => State > 0 && !requireDelivery;
        public override bool DisplayAsFailed => false;

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            byte selectedCaveIndex = msg.ReadByte();
            nestPosition = new Vector2(
                msg.ReadSingle(),
                msg.ReadSingle());
            if (selectedCaveIndex < 255 && Level.Loaded != null)
            {
                if (selectedCaveIndex < Level.Loaded.Caves.Count)
                {
                    Level.Loaded.Caves[selectedCaveIndex].DisplayOnSonar = true;
                    SpawnNestObjects(Level.Loaded, Level.Loaded.Caves[selectedCaveIndex]);
                }
                else
                {
                    DebugConsole.ThrowError($"Cave index out of bounds when reading nest mission data. Index: {selectedCaveIndex}, number of caves: {Level.Loaded.Caves.Count}");
                }
            }

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
