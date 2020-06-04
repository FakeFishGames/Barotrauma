using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AfflictionHusk : Affliction
    {
        partial void UpdateMessages()
        {
            switch (State)
            {
                case InfectionState.Dormant:
                    GUI.AddMessage(TextManager.Get("HuskDormant"), GUI.Style.Red);
                    break;
                case InfectionState.Transition:
                    GUI.AddMessage(TextManager.Get("HuskCantSpeak"), GUI.Style.Red);
                    break;
                case InfectionState.Active:
                    if (character.Params.UseHuskAppendage)
                    {
                        GUI.AddMessage(TextManager.GetWithVariable("HuskActivate", "[Attack]", GameMain.Config.KeyBindText(InputType.Attack)), GUI.Style.Red);
                    }
                    break;
                case InfectionState.Final:
                default:
                    break;
            }
        }
    }
}
