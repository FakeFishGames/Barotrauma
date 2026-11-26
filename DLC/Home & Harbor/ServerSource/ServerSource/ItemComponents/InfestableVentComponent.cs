using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor;

public partial class InfestableVentComponent
{
    private float prevSentInfestationProgress;
    private float sentTimeInfestationProgress;

    public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(IsInfested);
        msg.WriteSingle(InfestationProgress);
    }
}
