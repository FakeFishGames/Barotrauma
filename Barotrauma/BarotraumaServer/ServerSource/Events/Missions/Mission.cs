using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int missionState)
        {
            if (missionState >= Headers.Count && missionState >= Messages.Count) return;

            string header = missionState < Headers.Count ? Headers[missionState] : "";
            string message = missionState < Messages.Count ? Messages[missionState] : "";

            GameServer.Log(TextManager.Get("MissionInfo") + ": " + header + " - " + message, ServerLog.MessageType.ServerMessage);
        }
    }
}