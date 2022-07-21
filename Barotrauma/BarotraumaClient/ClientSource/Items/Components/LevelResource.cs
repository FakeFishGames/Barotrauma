using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            deattachTimer = msg.ReadSingle();
            if (deattachTimer >= DeattachDuration)
            {
                holdable.DeattachFromWall();
                trigger.Enabled = false;
            }
        }
    }
}
