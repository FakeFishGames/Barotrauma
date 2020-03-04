using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AfflictionHusk : Affliction
    {
        partial void UpdateMessages(float prevStrength, Character character)
        {
            if (Strength < Prefab.MaxStrength * 0.5f)
            {
                if (prevStrength % 10.0f > 0.05f && Strength % 10.0f < 0.05f)
                {
                    GUI.AddMessage(TextManager.Get("HuskDormant"), GUI.Style.Red);
                }
            }
            else if (Strength < Prefab.MaxStrength)
            {
                if (state == InfectionState.Dormant && Character.Controlled == character)
                {
                    GUI.AddMessage(TextManager.Get("HuskCantSpeak"), GUI.Style.Red);
                }
            }
            else if (state != InfectionState.Active && Character.Controlled == character)
            {
                GUI.AddMessage(TextManager.GetWithVariable("HuskActivate", "[Attack]", GameMain.Config.KeyBindText(InputType.Attack)),
                    GUI.Style.Red);
            }
        }
    }
}
