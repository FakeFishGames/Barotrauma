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
                List<Item> characterItems = characterDictionary[character];
                // items must be written in a specific sequence so that child items aren't written before their parents
                msg.Write((ushort)characterItems.Count());
                foreach (Item item in characterItems)
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory.Owner?.ID ?? Entity.NullEntityID, 0);
                }
            }
        }
    }
}
