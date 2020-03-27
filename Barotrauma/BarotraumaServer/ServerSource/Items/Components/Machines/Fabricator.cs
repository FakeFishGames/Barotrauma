using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            int itemIndex = msg.ReadRangedInteger(-1, fabricationRecipes.Count - 1);

            item.CreateServerEvent(this);

            if (!item.CanClientAccess(c)) return;

            if (itemIndex == -1)
            {
                CancelFabricating(c.Character);
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricationRecipes.IndexOf(fabricatedItem) == itemIndex) return;
                if (itemIndex < 0 || itemIndex >= fabricationRecipes.Count) return;

                StartFabricating(fabricationRecipes[itemIndex], c.Character);
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write((byte)State);
            msg.Write(timeUntilReady);
            int itemIndex = fabricatedItem == null ? -1 : fabricationRecipes.IndexOf(fabricatedItem);
            msg.WriteRangedInteger(itemIndex, -1, fabricationRecipes.Count - 1);
            UInt16 userID = fabricatedItem == null || user == null ? (UInt16)0 : user.ID;
            msg.Write(userID);
        }
    }
}
