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

            new GUIMessageBox(header, message, buttons: new string[0], type: GUIMessageBox.Type.InGame);
        }
    }
}
