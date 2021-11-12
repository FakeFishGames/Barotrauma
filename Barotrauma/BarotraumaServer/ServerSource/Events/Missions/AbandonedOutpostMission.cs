using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        private readonly List<Item> spawnedItems = new List<Item>();

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.Write((ushort)spawnedItems.Count);
            foreach (Item item in spawnedItems)
            {
                item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0);
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