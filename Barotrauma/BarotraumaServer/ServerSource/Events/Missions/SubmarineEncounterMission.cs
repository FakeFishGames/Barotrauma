using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    public partial class SubmarineEncounterMission : Mission
    {
        private const float EndDelay = 5.0f;
        private float endTimer;

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            if (subSpawnDisallowed)
            {
                msg.WriteByte(0);
                return;
            }

            // duplicate code from escortmission, should possibly be combined, though additional loot items might be added so maybe not
            if (characters.Count == 0)
            {
                throw new InvalidOperationException("Server attempted to write escort mission data when no characters had been spawned.");
            }

            msg.WriteByte((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                msg.WriteUInt16((ushort)characterItems[character].Count);
                foreach (Item item in characterItems[character])
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? Entity.NullEntityID, 0, item.ParentInventory?.FindIndex(item) ?? -1);
                }
            }
        }
    }
}
