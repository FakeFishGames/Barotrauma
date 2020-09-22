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

        private ulong serverEventId = 0;
        public override void ServerAppendExtraData(ref object[] extraData)
        {
            //ensuring the uniqueness of this event is
            //required for the fabricator to sync correctly;
            //otherwise, the event manager would incorrectly
            //assume that the client actually has the latest state
            Array.Resize(ref extraData, 4);
            extraData[2] = serverEventId;
            extraData[3] = State;
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            FabricatorState stateAtEvent = (FabricatorState)extraData[3];
            msg.Write((byte)stateAtEvent);
            msg.Write(timeUntilReady);
            int itemIndex = fabricatedItem == null ? -1 : fabricationRecipes.IndexOf(fabricatedItem);
            msg.WriteRangedInteger(itemIndex, -1, fabricationRecipes.Count - 1);
            UInt16 userID = fabricatedItem == null || user == null ? (UInt16)0 : user.ID;
            msg.Write(userID);
        }
    }
}
