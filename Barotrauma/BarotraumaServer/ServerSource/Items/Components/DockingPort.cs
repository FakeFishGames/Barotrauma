using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(docked);
            if (docked)
            {
                msg.WriteUInt16(DockingTarget.item.ID);
                msg.WriteBoolean(IsLocked);
            }
        }
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            var allowOutpostAutoDocking = (AllowOutpostAutoDocking)msg.ReadByte();
            if (outpostAutoDockingPromptShown &&
                (GameMain.GameSession?.Campaign?.AllowedToManageCampaign(c, ClientPermissions.ManageMap) ?? false))
            {
                this.allowOutpostAutoDocking = allowOutpostAutoDocking;
            }
        }

    }
}
