using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class LineDrawer : Powered, IClientSerializable
{
    partial void UpdateProjSpecific(float deltaTime, Camera cam)
    {
        if (turretComponent?.User is Character)
        {
            if (Math.Abs(lastSentRange - currentRange) > SendRangeThreshold &&
                Timing.TotalTimeUnpaused > lastSentRangeTime + SendRangeInterval)
            {
                lastSentRange = currentRange;
                lastSentRangeTime = Timing.TotalTimeUnpaused;
                item.CreateServerEvent(this);
            }
        }
    }

    public void ServerEventRead(IReadMessage msg, Client c)
    {
        float newRange = msg.ReadSingle();
        if (turretComponent?.User == c.Character)
        {
            currentRange = newRange;
        }
    }

    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteSingle(currentRange);
    }
}
