using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Wearable
    {
        private void GetDamageModifierText(ref string description, float damageMultiplier, string afflictionIdentifier)
        {
            int roundedValue = (int)Math.Round((1 - damageMultiplier) * 100);
            if (roundedValue == 0) { return; }
            string colorStr = XMLExtensions.ColorToString(GUI.Style.Green);
            description += $"\n  ‖color:{colorStr}‖{roundedValue.ToString("-0;+#")}%‖color:end‖ {AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier.Equals(afflictionIdentifier, StringComparison.OrdinalIgnoreCase))?.Name ?? afflictionIdentifier}";
        }

        public override void AddTooltipInfo(ref string name, ref string description)
        {
            if (damageModifiers.Any(d => !MathUtils.NearlyEqual(d.DamageMultiplier, 1f)) || SkillModifiers.Any())
            {
                description += "\n";
            }

            if (damageModifiers.Any())
            {
                foreach (DamageModifier damageModifier in damageModifiers)
                {
                    if (MathUtils.NearlyEqual(damageModifier.DamageMultiplier, 1f))
                    {
                        continue;
                    }

                    foreach (string afflictionIdentifier in damageModifier.ParsedAfflictionIdentifiers)
                    {
                        GetDamageModifierText(ref description, damageModifier.DamageMultiplier, afflictionIdentifier);
                    }
                    foreach (string afflictionIdentifier in damageModifier.ParsedAfflictionTypes)
                    {
                        GetDamageModifierText(ref description, damageModifier.DamageMultiplier, afflictionIdentifier);
                    }
                }
            }
            if (SkillModifiers.Any())
            {
                foreach (var skillModifier in SkillModifiers)
                {
                    string colorStr = XMLExtensions.ColorToString(GUI.Style.Green);
                    int roundedValue = (int)Math.Round(skillModifier.Value);
                    if (roundedValue == 0) { continue; }
                    description += $"\n  ‖color:{colorStr}‖{roundedValue.ToString("+0;-#")}‖color:end‖ {TextManager.Get("SkillName." + skillModifier.Key, true) ?? skillModifier.Key}";
                }
            }
        }
    }
}
