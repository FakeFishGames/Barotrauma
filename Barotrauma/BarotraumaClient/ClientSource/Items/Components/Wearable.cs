using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Wearable : Pickable, IServerSerializable
    {
        private void GetDamageModifierText(ref LocalizedString description, DamageModifier damageModifier, Identifier afflictionIdentifier)
        {
            int roundedValue = (int)Math.Round((1 - damageModifier.DamageMultiplier * damageModifier.ProbabilityMultiplier) * 100);
            if (roundedValue == 0) { return; }
            string colorStr = XMLExtensions.ToStringHex(GUIStyle.Green);

            LocalizedString afflictionName =
                AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == afflictionIdentifier)?.Name ??
                TextManager.Get($"afflictiontype.{afflictionIdentifier}").Fallback(afflictionIdentifier.Value);

            if (!description.IsNullOrWhiteSpace()) { description += '\n'; }
            description += $"  ‖color:{colorStr}‖{roundedValue.ToString("-0;+#")}%‖color:end‖ {afflictionName}";
        }
        
        public override void AddTooltipInfo(ref LocalizedString name, ref LocalizedString description)
        {
            if (damageModifiers.Any())
            {
                foreach (DamageModifier damageModifier in damageModifiers)
                {
                    if (MathUtils.NearlyEqual(damageModifier.DamageMultiplier, 1f))
                    {
                        continue;
                    }

                    foreach (Identifier afflictionIdentifier in damageModifier.ParsedAfflictionIdentifiers)
                    {
                        GetDamageModifierText(ref description, damageModifier, afflictionIdentifier);
                    }
                    foreach (Identifier afflictionType in damageModifier.ParsedAfflictionTypes)
                    {
                        GetDamageModifierText(ref description, damageModifier, afflictionType);
                    }
                }
            }
            if (SkillModifiers.Any())
            {
                foreach (var skillModifier in SkillModifiers)
                {
                    string colorStr = XMLExtensions.ToStringHex(GUIStyle.Green);
                    int roundedValue = (int)Math.Round(skillModifier.Value);
                    if (roundedValue == 0) { continue; }
                    if (!description.IsNullOrWhiteSpace()) { description += '\n'; }
                    description += $"  ‖color:{colorStr}‖{roundedValue.ToString("+0;-#")}‖color:end‖ {TextManager.Get($"SkillName.{skillModifier.Key}").Fallback(skillModifier.Key.Value)}";
                }
            }
        }
    }
}
