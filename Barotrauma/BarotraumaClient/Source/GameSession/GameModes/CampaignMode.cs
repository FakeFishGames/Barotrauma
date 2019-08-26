using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (Mission == null) return;

            new GUIMessageBox(Mission.Name, Mission.Description, new Vector2(0.25f, 0.0f), new Point(400, 200))
            {
                UserData = "missionstartmessage"
            };
        }
    }
}
