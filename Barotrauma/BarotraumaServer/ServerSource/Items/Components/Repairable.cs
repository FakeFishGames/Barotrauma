using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        private Character prevLoggedFixer;
        private FixActions prevLoggedFixAction;

        public override void OnMapLoaded()
        {
            //let the clients know the initial deterioration delay
            item.CreateServerEvent(this);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            if (c.Character == null) { return; }
            var requestedFixAction = (FixActions)msg.ReadRangedInteger(0, 2);
            var QTESuccess = msg.ReadBoolean();
            if (requestedFixAction != FixActions.None)
            {
                if (!c.Character.IsTraitor && requestedFixAction == FixActions.Sabotage)
                {
                    if (GameSettings.CurrentConfig.VerboseLogging)
                    {
                        DebugConsole.Log($"Non traitor \"{c.Character.Name}\" attempted to sabotage item.");
                    }
                    requestedFixAction = FixActions.Repair;
                }

                if (CurrentFixer == null || CurrentFixer == c.Character && requestedFixAction != currentFixerAction)
                {
                    StartRepairing(c.Character, requestedFixAction);
                    item.CreateServerEvent(this);
                }
            }
            else
            {
                RepairBoost(QTESuccess);
                item.CreateServerEvent(this);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteSingle(deteriorationTimer);
            msg.WriteSingle(deteriorateAlwaysResetTimer);
            msg.WriteBoolean(DeteriorateAlways);
            msg.WriteSingle(tinkeringDuration);
            msg.WriteSingle(tinkeringStrength);
            msg.WriteBoolean(tinkeringPowersDevices);
            msg.WriteUInt16(CurrentFixer == null ? (ushort)0 : CurrentFixer.ID);
            msg.WriteRangedInteger((int)currentFixerAction, 0, 2);
        }
    }
}
