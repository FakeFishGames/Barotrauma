using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        public void ShowMessage(int index)
        {
            if (index >= headers.Count && index >= messages.Count) return;

            string header = index < headers.Count ? headers[index] : "";
            string message = index < messages.Count ? messages[index] : "";

            GameServer.Log("Mission info: " + header + " - " + message, ServerLog.MessageType.ServerMessage);

            new GUIMessageBox(header, message);
        }
    }
}
