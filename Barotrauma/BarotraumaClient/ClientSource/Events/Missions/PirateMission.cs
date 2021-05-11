using Barotrauma.Networking;

namespace Barotrauma
{
    partial class PirateMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            // duplicate code from escortmission, should possibly be combined, though additional loot items might be added so maybe not
            byte characterCount = msg.ReadByte();

            for (int i = 0; i < characterCount; i++)
            {
                characters.Add(Character.ReadSpawnData(msg));
                ushort itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Item.ReadSpawnData(msg);

                }
            }
            if (characters.Contains(null))
            {
                throw new System.Exception("Error in PirateMission.ClientReadInitial: character list contains null (mission: " + Prefab.Identifier + ")");
            }

            if (characters.Count != characterCount)
            {
                throw new System.Exception("Error in PirateMission.ClientReadInitial: character count does not match the server count (" + characterCount + " != " + characters.Count + "mission: " + Prefab.Identifier + ")");
            }
        }
    }
}
