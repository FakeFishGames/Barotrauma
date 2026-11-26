using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

public partial class LinkToolComponent
{
    public void ServerEventRead(IReadMessage msg, Client c)
    {
        UInt16 firstId = msg.ReadUInt16();
        UInt16 secondId = msg.ReadUInt16();
        bool unlink = msg.ReadBoolean();

        if (Entity.FindEntityByID(firstId) is not Item first || 
            Entity.FindEntityByID(secondId) is not Item second || 
            c.Character == null || 
            !c.Character.HasEquippedItem(Item) || 
            !CheckLinkLimits(first, second, unlink, out var _))
        {
            return;
        }

        LinkOrUnlinkItems(first, second, unlink);

        Item.CreateServerEvent(this, new LinkEventData(first, second, unlink));
    }

    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        var linkEventData = ExtractEventData<LinkEventData>(extraData);

        msg.WriteUInt16(linkEventData.First.ID);
        msg.WriteUInt16(linkEventData.Second.ID);
        msg.WriteBoolean(linkEventData.Unlink);
    }
}