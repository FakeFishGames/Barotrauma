using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class PirateMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            // duplicate code from escortmission, should possibly be combined, though additional loot items might be added so maybe not
            if (characters.Count == 0)
            {
                throw new InvalidOperationException("Server attempted to write escort mission data when no characters had been spawned.");
            }

            msg.Write((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                msg.Write((ushort)characterItems[character].Count());
                foreach (Item item in characterItems[character])
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? Entity.NullEntityID, 0);
                }
            }
        }
    }
}
