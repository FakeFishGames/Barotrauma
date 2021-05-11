using Barotrauma.Networking;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            if (characters.Count == 0)
            {
                throw new InvalidOperationException("Server attempted to write AbandonedOutpostMission data when no characters had been spawned.");
            }

            msg.Write((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                msg.Write(requireKill.Contains(character));
                msg.Write(requireRescue.Contains(character));
                msg.Write((ushort)characterItems[character].Count());
                foreach (Item item in characterItems[character])
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? Entity.NullEntityID, 0);
                }
            }
        }
    }
}