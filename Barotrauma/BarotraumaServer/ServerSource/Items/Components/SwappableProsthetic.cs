#nullable enable

using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
namespace Barotrauma;

internal sealed partial class SwappableProsthetic : ItemComponent
{
    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        if (linkedItems == null)
        {
            DebugConsole.ThrowError($"Tried to write event data for {nameof(SwappableProsthetic)} ({item.Name}) before linked items had been initialized.\n" + Environment.StackTrace);
            return;
        }

        msg.WriteByte((byte)linkedItems.Count);
        for (int i = 0; i <  linkedItems.Count; i++)
        {
            msg.WriteUInt16(linkedItems[i].ID);
        }

        msg.WriteRangedInteger(Mode, 0, linkedItems.Count);
        msg.WriteUInt32((UInt32)LastSlotType);
    }

    public void ServerEventRead(IReadMessage msg, Client c)
    {
        if (linkedItems == null)
        {
            DebugConsole.ThrowError($"Tried to read event data for {nameof(SwappableProsthetic)} ({item.Name}) before linked items had been initialized.\n" + Environment.StackTrace);
            return;
        }
        int newMode = msg.ReadRangedInteger(0, linkedItems.Count);
        if (item.ParentInventory?.Owner is Character wearer && c.Character == wearer && item.CanClientAccess(c))
        {
            SetMode(wearer, newMode);
        }
        // Create an event to notify other clients about the changes
        item.CreateServerEvent(this);
    }
}
