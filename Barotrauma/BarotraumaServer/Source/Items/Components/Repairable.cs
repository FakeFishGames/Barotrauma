using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        void InitProjSpecific()
        {
            //let the clients know the initial deterioration delay
            item.CreateServerEvent(this);
        }

        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            if (c.Character == null) return;
            var fixAction = (FixActions)msg.ReadRangedInteger(0, 2);
            if (!c.Character.IsTraitor && fixAction == FixActions.Sabotage)
            {
                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.Log($"Non traitor \"{c.Character.Name}\" attempted to sabotage item.");
                }
                fixAction = FixActions.Repair;
            }
            StartRepairing(c.Character, fixAction);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(deteriorationTimer);
            msg.Write(deteriorateAlwaysResetTimer);
            msg.Write(DeteriorateAlways);
        }
    }
}
