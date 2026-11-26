using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor;

public partial class SingularityComponent : ItemComponent
{
    private float networkEventCooldown;
    const float NetworkEventInterval = 1.0f;

    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(isContained);
        msg.WriteRangedSingle(strength, MinStrength, MaxStrength, bitCount: 8);
        if (!isContained)
        {
            msg.WriteSingle(movementTimer);
            msg.WriteSingle(currentVelocity.X);
            msg.WriteSingle(currentVelocity.Y);
        }
    }

    partial void UpdateProjSpecific(float deltaTime)
    {
        networkEventCooldown -= deltaTime;
        if (networkEventCooldown < 0.0f)
        {
            item.CreateServerEvent(this);
            networkEventCooldown = NetworkEventInterval;
        }        
    }
}