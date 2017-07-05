using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        public bool MaintainPos;
        public bool LevelStartSelected;
        public bool LevelEndSelected;
    }
}
