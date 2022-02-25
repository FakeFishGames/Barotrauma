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
            uint recipeHash = msg.ReadUInt32();

            item.CreateServerEvent(this);

            if (!item.CanClientAccess(c)) return;

            if (recipeHash == 0)
            {
                CancelFabricating(c.Character);
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricatedItem.RecipeHash == recipeHash) { return; }
                if (recipeHash == 0) { return; }

                StartFabricating(fabricationRecipes[recipeHash], c.Character);
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
            uint recipeHash = fabricatedItem?.RecipeHash ?? 0;
            msg.Write(recipeHash);
            UInt16 userID = fabricatedItem is null || user is null ? (UInt16)0 : user.ID;
            msg.Write(userID);
        }
    }
}
