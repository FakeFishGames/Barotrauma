using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma;

partial class MessageBoxAction : EventAction
{
    partial void UpdateProjSpecific()
    {
        if (!TargetTag.IsEmpty)
        {
            IEnumerable<Entity> entities = ParentEvent.GetTargets(TargetTag);
            foreach (Entity entity in entities)
            {
                if (entity is not Character character || !character.IsRemotePlayer) { continue; }
                Client targetClient = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                if (targetClient != null)
                {
                    ServerWrite(targetClient);
                }
            }
        }
        else
        {
            foreach (Client targetClient in GameMain.Server.ConnectedClients)
            {
                ServerWrite(targetClient);
            }
        }
    }

    public void ServerWrite(Client client)
    {
        IWriteMessage outmsg = new WriteOnlyMessage();
        outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
        outmsg.WriteByte((byte)EventManager.NetworkEventType.MESSAGEBOXACTION);
        outmsg.WriteNetSerializableStruct(ToMessageBoxData());
        GameMain.Server?.ServerPeer?.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
    }
}