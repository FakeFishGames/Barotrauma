#nullable enable
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    internal partial class PowerDistributor : PowerTransfer, IServerSerializable, IClientSerializable
    {
        #region Networking
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            SharedEventRead(msg, out EventType eventType, out PowerGroup powerGroup, out string newName, out float newRatio);

            if (item.CanClientAccess(c))
            {
                switch (eventType)
                {
                    case EventType.NameChange:
                        powerGroup.Name = newName;
                        break;
                    case EventType.RatioChange:
                        powerGroup.SupplyRatio = newRatio;
                        GameServer.Log($"{GameServer.CharacterLogName(c.Character)} changed supply ratio of power group \"{powerGroup.Name}\" to \"{powerGroup.SupplyRatio}\"", ServerLog.MessageType.ItemInteraction);
                        break;
                }
            }

            item.CreateServerEvent(this, new EventData(powerGroup, eventType));
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null) => SharedEventWrite(msg, extraData);
        #endregion
    }
}
