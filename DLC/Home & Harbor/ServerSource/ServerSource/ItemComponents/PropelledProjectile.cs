using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class PropelledProjectile : Projectile
{
    #region Networking
    private readonly record struct PropelledProjectileEventData(EventType Type) : IEventData;

    public override bool ValidateEventData(NetEntityEvent.IData data)
        => TryExtractEventData<PropelledProjectileEventData>(data, out _);

    public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        base.ServerEventWrite(msg, c, extraData);

        PropelledProjectileEventData data = ExtractEventData<PropelledProjectileEventData>(extraData);
        msg.WriteRangedInteger((int)data.Type, 0, Enum.GetValues<EventType>().Length - 1);

        switch (data.Type)
        {
            case EventType.InitTracking:
                msg.WriteUInt16(turret?.Item.ID ?? Entity.NullEntityID);
                break;
            case EventType.InitOscillation:
                msg.WriteSingle(oscillationTimer);
                break;
        }
    }
    #endregion
}