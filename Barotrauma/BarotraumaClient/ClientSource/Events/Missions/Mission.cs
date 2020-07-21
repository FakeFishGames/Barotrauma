using Barotrauma.Networking;
using System.Collections.Generic;

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

            CoroutineManager.StartCoroutine(ShowMessageBoxAfterRoundSummary(header, message));
        }

        private IEnumerable<object> ShowMessageBoxAfterRoundSummary(string header, string message)
        {
            while (GUIMessageBox.VisibleBox?.UserData is RoundSummary)
            {
                yield return new WaitForSeconds(1.0f);
            }
            new GUIMessageBox(header, message, buttons: new string[0], type: GUIMessageBox.Type.InGame, icon: Prefab.Icon)
            {
                IconColor = Prefab.IconColor
            };
            yield return CoroutineStatus.Success;
        }

        public void ClientRead(IReadMessage msg)
        {
            State = msg.ReadInt16();
        }

        public abstract void ClientReadInitial(IReadMessage msg);
    }
}
