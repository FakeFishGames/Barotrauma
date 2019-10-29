using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int index)
        {
            if (index >= Headers.Count && index >= Messages.Count) { return; }

            string header = index < Headers.Count ? Headers[index] : "";
            string message = index < Messages.Count ? Messages[index] : "";

            new GUIMessageBox(header, message, buttons: new string[0], type: GUIMessageBox.Type.InGame, icon: Prefab.Icon)
            {
                IconColor = Prefab.IconColor
            };
        }

        public void ClientRead(IReadMessage msg)
        {
            State = msg.ReadInt16();
        }
    }
}
