using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Quality : ItemComponent
    {
        public override void AddTooltipInfo(ref LocalizedString name, ref LocalizedString description)
        {
            foreach (var statValue in statValues)
            {
                int roundedValue = (int)Math.Round(statValue.Value * qualityLevel * 100);
                if (roundedValue == 0) { return; }
                string colorStr = XMLExtensions.ColorToString(GUIStyle.Green);
                description += $"\n  ‖color:{colorStr}‖{roundedValue.ToString("+0;-#")}%‖color:end‖ {TextManager.Get("qualitystattypenames." + statValue.Key.ToString()).Fallback(statValue.Key.ToString())}";
            }
        }
    }
}
