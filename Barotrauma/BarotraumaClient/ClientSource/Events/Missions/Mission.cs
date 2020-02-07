using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Count && messageIndex >= Messages.Count) { return; }
            if (messageIndex < 0) { return; }

            string header = messageIndex < Headers.Count ? Headers[messageIndex] : "";
            string message = messageIndex < Messages.Count ? Messages[messageIndex] : "";

            new GUIMessageBox(header, message, buttons: new string[0], type: GUIMessageBox.Type.InGame, icon: Prefab.Icon)
            {
                IconColor = Prefab.IconColor
            };
        }

        public void ClientRead(IReadMessage msg)
        {
            State = msg.ReadInt16();
        }

        public abstract void ClientReadInitial(IReadMessage msg);
    }
}
