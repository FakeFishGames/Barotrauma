using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int index)
        {
            if (index >= Headers.Count && index >= Messages.Count) return;

            string header = index < Headers.Count ? Headers[index] : "";
            string message = index < Messages.Count ? Messages[index] : "";

            GameServer.Log(TextManager.Get("MissionInfo") + ": " + header + " - " + message, ServerLog.MessageType.ServerMessage);
        }
    }
}