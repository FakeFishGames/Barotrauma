using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    partial class TraitorMissionResult
    {
        public TraitorMissionResult(IReadMessage inc)
        {
            MissionIdentifier = inc.ReadIdentifier();
            EndMessage = inc.ReadString();
            Success = inc.ReadBoolean();
            byte characterCount = inc.ReadByte();
            for (int i = 0; i < characterCount; i++)
            {
                UInt16 characterID = inc.ReadUInt16();
                var character = Entity.FindEntityByID(characterID) as Character;
                if (character != null) { Characters.Add(character); }
            }
        }
    }
}
