using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Scanner : ItemComponent, IServerSerializable
    {
        partial void UpdateProjSpecific()
        {
            if (Holdable != null && Holdable.Attached && (AlwaysDisplayProgressBar || DisplayProgressBar) && !IsScanCompleted)
            {
                Character.Controlled?.UpdateHUDProgressBar(this,
                    item.WorldPosition,
                    ScanTimer / ScanDuration,
                    GUIStyle.Red, GUIStyle.Green,
                    textTag: "progressbar.scanning");
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            bool wasScanCompletedPreviously = IsScanCompleted;
            scanTimer = msg.ReadSingle();
            if (!wasScanCompletedPreviously && IsScanCompleted)
            {
                OnScanCompleted?.Invoke(this);
            }
        }
    }
}
