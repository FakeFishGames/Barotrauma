using Barotrauma.Networking;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        public override int State
        {
            get { return base.State; }
            protected set
            {
                if (state != value)
                {
                    base.State = value;
                    if (state == HostagesKilledState && !string.IsNullOrEmpty(hostagesKilledMessage))
                    {
                        new GUIMessageBox(string.Empty, hostagesKilledMessage, buttons: new string[0], type: GUIMessageBox.Type.InGame, icon: Prefab.Icon, parseRichText: true)
                        {
                            IconColor = Prefab.IconColor
                        };
                    }
                }
            }
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            byte characterCount = msg.ReadByte();

            for (int i = 0; i < characterCount; i++)
            {
                Character character = Character.ReadSpawnData(msg);
                characters.Add(character);
                if (msg.ReadBoolean()) { requireKill.Add(character); }
                if (msg.ReadBoolean()) 
                { 
                    requireRescue.Add(character);
#if CLIENT
                    GameMain.GameSession.CrewManager.AddCharacterToCrewList(character);
#endif
                }
                ushort itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Item.ReadSpawnData(msg);
                }
            }
            if (characters.Contains(null))
            {
                throw new System.Exception("Error in AbandonedOutpostMission.ClientReadInitial: character list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (characters.Count != characterCount)
            {
                throw new System.Exception("Error in AbandonedOutpostMission.ClientReadInitial: character count does not match the server count (" + characters + " != " + characters.Count + "mission: " + Prefab.Identifier + ")");
            }
        }
    }
}