#nullable enable

using Barotrauma.Networking;

namespace Barotrauma
{
    sealed partial class TraitorManager
    {
        public static void ClientRead(IReadMessage msg)
        {
            //unused, but could be worth keeping in the messages regardless in case a mod wants to use these for something
            TraitorEvent.State state = (TraitorEvent.State)msg.ReadByte();
            Identifier eventIdentifier = msg.ReadIdentifier();
            if (GameMain.Client?.Character == null)
            {
                DebugConsole.AddSafeError("Received a traitor update when not controlling a character.");
                return;
            }
            GameMain.Client.Character.IsTraitor = true;            
        }
    }
}