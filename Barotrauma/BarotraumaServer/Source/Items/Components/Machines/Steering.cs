using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        // TODO: an enumeration would be much cleaner
        public bool MaintainPos;
        public bool LevelStartSelected;
        public bool LevelEndSelected;

        public bool UnsentChanges
        {
            get { return unsentChanges; }
            set { unsentChanges = value; }
        }
    }
}
