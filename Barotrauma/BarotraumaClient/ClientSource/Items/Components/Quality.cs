using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Quality : ItemComponent
    {
        public override void AddTooltipInfo(ref string name, ref string description)
        {
            foreach (var statValue in statValues)
            {
                int roundedValue = (int)Math.Round(statValue.Value * qualityLevel * 100);
                if (roundedValue == 0) { return; }
                string colorStr = XMLExtensions.ColorToString(GUI.Style.Green);
                description += $"\n  ‖color:{colorStr}‖{roundedValue.ToString("+0;-#")}%‖color:end‖ {TextManager.Get("qualitystattypenames." + statValue.Key.ToString(), true) ?? statValue.Key.ToString()}";
            }
        }
    }
}
