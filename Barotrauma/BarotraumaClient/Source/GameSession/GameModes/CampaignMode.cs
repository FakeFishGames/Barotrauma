using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (Mission == null) return;

            new GUIMessageBox(Mission.Name, Mission.Description, new string[0], type: GUIMessageBox.Type.InGame, icon: Mission.Prefab.Icon)
            {
                IconColor = Mission.Prefab.IconColor,
                UserData = "missionstartmessage"
            };
        }
    }
}
