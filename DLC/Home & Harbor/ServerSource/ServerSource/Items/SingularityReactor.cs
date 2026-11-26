using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

partial class SingularityReactor
{
    private Client? blameOnBroken;

    private float? nextServerLogWriteTime;
    private float lastServerLogWriteTime;

    public override void ServerEventRead(IReadMessage msg, Client c)
    {
        bool autoTemp = msg.ReadBoolean();
        bool powerOn = msg.ReadBoolean();
        float feedingRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        float containmentRate = msg.ReadRangedSingle(0.0f, 100.0f, 8);
        float containmentFieldAmplitude = msg.ReadRangedSingle(MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        float containmentFieldOffset = msg.ReadRangedSingle(MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        float containmentFieldMatchError = msg.ReadRangedSingle(0.0f, 1.0f, 16);

        if (!item.CanClientAccess(c)) { return; }

        IsActive = true;

        if (!autoTemp && AutoTemp) { blameOnBroken = c; }
        if (feedingRate > TargetFeedingRate) { blameOnBroken = c; }
        if (!PowerOn && powerOn) { blameOnBroken = c; }

        AutoTemp = autoTemp;
        PowerOn = powerOn;
        TargetFeedingRate = feedingRate;
        TargetContainmentRate = containmentRate;
        this.containmentFieldAmplitude = containmentFieldAmplitude;
        this.containmentFieldOffset = containmentFieldOffset;
        this.containmentFieldMatchError = containmentFieldMatchError;

        LastUser = c.Character;
        if (nextServerLogWriteTime == null)
        {
            nextServerLogWriteTime = Math.Max(lastServerLogWriteTime + 1.0f, (float)Timing.TotalTime);
        }

        //need to create a server event to notify all clients of the changed state
        unsentChanges = true;
    }

    public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(AutoTemp);
        msg.WriteBoolean(PowerOn);
        msg.WriteUInt16(Singularity?.Item.ID ?? Entity.NullEntityID);
        msg.WriteRangedSingle(TargetFeedingRate, 0.0f, 100.0f, 8);
        msg.WriteRangedSingle(TargetContainmentRate, 0.0f, 100.0f, 8);
        msg.WriteRangedSingle(containmentFieldAmplitude, MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        msg.WriteRangedSingle(containmentFieldOffset, MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        msg.WriteRangedSingle(containmentFieldTargetAmplitude, MinContainmentFieldAmplitude, MaxContainmentFieldAmplitude, 16);
        msg.WriteRangedSingle(containmentFieldTargetOffset, MinContainmentFieldOffset, MaxContainmentFieldOffset, 16);
        msg.WriteRangedSingle(degreeOfSuccess, 0.0f, 1.0f, 8);
    }
}
