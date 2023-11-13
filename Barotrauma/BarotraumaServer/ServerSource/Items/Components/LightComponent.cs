using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable
    {
        private CoroutineHandle sendStateCoroutine;
        private bool lastSentState;
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
            if (lastSentState != IsActive) { item.CreateServerEvent(this); }
            yield return CoroutineStatus.Success;
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(IsActive);
            lastSentState = IsActive;
        }
    }
}
