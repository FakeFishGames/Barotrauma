using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class HuskInfection
    {
        partial void UpdateProjSpecific(float prevTimer, Character character)
        {
            if (IncubationTimer < 0.5f)
            {
                if (prevTimer % 0.1f > 0.05f && IncubationTimer % 0.1f < 0.05f)
                {
                    GUI.AddMessage(TextManager.Get("HuskDormant"), Color.Red, 4.0f);
                }
            }
            else if (IncubationTimer < 1.0f)
            {
                if (state == InfectionState.Dormant && Character.Controlled == character)
                {
                    new GUIMessageBox("", TextManager.Get("HuskCantSpeak"));
                }
            }
            else if (state != InfectionState.Active && Character.Controlled == character)
            {
                new GUIMessageBox("", TextManager.Get("HuskActivate"));
            }
        }
    }
}
