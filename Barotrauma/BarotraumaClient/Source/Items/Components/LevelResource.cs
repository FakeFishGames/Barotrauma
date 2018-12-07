using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            DeattachTimer = msg.ReadSingle();
            if (deattachTimer >= DeattachDuration)
            {
                holdable.DeattachFromWall();
            }
        }
    }
}
