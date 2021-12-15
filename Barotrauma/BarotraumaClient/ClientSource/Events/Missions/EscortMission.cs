using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EscortMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);

            byte characterCount = msg.ReadByte();

            for (int i = 0; i < characterCount; i++)
            {
                Character character = Character.ReadSpawnData(msg);
                characters.Add(character);
                if (msg.ReadBoolean())
                {
                    terroristCharacters.Add(character);
                }
                ushort itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Item.ReadSpawnData(msg);
                }
            }
            if (characters.Contains(null))
            {
                throw new System.Exception("Error in EscortMission.ClientReadInitial: character list contains null (mission: " + Prefab.Identifier + ")");
            }

            if (characters.Count != characterCount)
            {
                throw new System.Exception("Error in EscortMission.ClientReadInitial: character count does not match the server count (" + characterCount + " != " + characters.Count + "mission: " + Prefab.Identifier + ")");
            }
            InitCharacters();
        }
    }
}
