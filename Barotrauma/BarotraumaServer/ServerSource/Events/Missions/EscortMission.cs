using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class EscortMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            if (characters.Count == 0)
            {
                throw new InvalidOperationException("Server attempted to write escort mission data when no characters had been spawned.");
            }

            msg.Write((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                msg.Write(terroristCharacters.Contains(character));
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
