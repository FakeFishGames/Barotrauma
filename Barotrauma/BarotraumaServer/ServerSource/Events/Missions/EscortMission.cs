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
            base.ServerWriteInitial(msg, c);

            if (characters.Count == 0)
            {
                throw new InvalidOperationException("Server attempted to write escort mission data when no characters had been spawned.");
            }

            msg.WriteByte((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                msg.WriteBoolean(terroristCharacters.Contains(character));
                msg.WriteUInt16((ushort)characterItems[character].Count());
                foreach (Item item in characterItems[character])
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? Entity.NullEntityID, 0, item.ParentInventory?.FindIndex(item) ?? -1);
                }
            }
        }
    }
}
