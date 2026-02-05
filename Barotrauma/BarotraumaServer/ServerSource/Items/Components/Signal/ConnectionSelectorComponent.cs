using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class ConnectionSelectorComponent : ItemComponent
    {
        private CoroutineHandle sendStateCoroutine;
        private int lastSentConnectionIndex;
        private float sendStateTimer;

        partial void OnStateChanged()
        {
            sendStateTimer = 0.5f;
            if (sendStateCoroutine == null)
            {
                sendStateCoroutine = CoroutineManager.StartCoroutine(SendStateAfterDelay());
            }
        }

        private IEnumerable<CoroutineStatus> SendStateAfterDelay()
        {
            while (sendStateTimer > 0.0f)
            {
                sendStateTimer -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            if (item.Removed || GameMain.NetworkMember == null)
            {
                yield return CoroutineStatus.Success;
            }

            sendStateCoroutine = null;
            if (lastSentConnectionIndex != selectedConnectionIndex)
            {
                item.CreateServerEvent(this);
            }
            yield return CoroutineStatus.Success;
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedInteger(selectedConnectionIndex, 0, 255);
            lastSentConnectionIndex = selectedConnectionIndex;
        }
    }
}
