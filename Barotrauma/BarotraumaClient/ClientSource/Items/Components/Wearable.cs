using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Wearable
    {
        private void GetDamageModifierText(ref string description, float damageMultiplier, string afflictionIdentifier)
        {
            string colorStr = XMLExtensions.ColorToString(GUI.Style.Green);
            description += $"\n  ‖color:{colorStr}‖-{Math.Round((1 - damageMultiplier) * 100)}%‖color:end‖ {TextManager.Get("AfflictionName." + afflictionIdentifier, true) ?? afflictionIdentifier}";
        }

        public override void AddTooltipInfo(ref string description)
        {
            if (damageModifiers.Any(d => d.DamageMultiplier != 1f) || SkillModifiers.Any())
            {
                description += "\n";
            }

            if (damageModifiers.Any())
            {
                foreach (DamageModifier damageModifier in damageModifiers)
                {
                    if (damageModifier.DamageMultiplier == 1f)
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
                    description += $"\n  ‖color:{colorStr}‖+{skillModifier.Value}‖color:end‖ {TextManager.Get("SkillName." + skillModifier.Key, true) ?? skillModifier.Key}";
                }
            }
        }
    }
}
