using Barotrauma.Networking;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            uint recipeHash = msg.ReadUInt32();

            item.CreateServerEvent(this);

            if (!item.CanClientAccess(c)) { return; }

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

        private readonly struct EventData : IEventData
        {
            public readonly ulong ServerEventId;
            public readonly FabricatorState State;

            public EventData(ulong serverEventId, FabricatorState state)
            {
                //ensuring the uniqueness of this event is
                //required for the fabricator to sync correctly;
                //otherwise, the event manager would incorrectly
                //assume that the client actually has the latest state
                ServerEventId = serverEventId;
                State = state;
            }
        }
        
        public override IEventData ServerGetEventData()
            => new EventData(serverEventId, State);

        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<EventData>(data, out _);

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            var componentData = ExtractEventData<EventData>(extraData);
            msg.WriteByte((byte)componentData.State);
            msg.WriteSingle(timeUntilReady);
            uint recipeHash = fabricatedItem?.RecipeHash ?? 0;
            msg.WriteUInt32(recipeHash);
            UInt16 userId = fabricatedItem is null || user is null ? (UInt16)0 : user.ID;
            msg.WriteUInt16(userId);

            var reachedLimits = fabricationLimits.Where(kvp => kvp.Value <= 0);
            msg.WriteUInt16((ushort)reachedLimits.Count());
            foreach (var kvp in reachedLimits)
            {
                msg.WriteUInt32(kvp.Key);
            }
        }
    }
}
