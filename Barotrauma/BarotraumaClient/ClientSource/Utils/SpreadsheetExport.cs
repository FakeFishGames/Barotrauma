#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.IO;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    public class SpreadsheetExport
    {
        private const char separator = ',';
        private const string debugIdentifier = "_spreadsheet";

        public static void Export()
        {
            XDocument doc = new XDocument();
            if (doc.Root == null)
            {
                doc.Add(new XElement("Content"));
            }

            XElement root = doc.Root!;

            foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
            {
                XElement itemElement = new XElement("Item",
                    new XAttribute("identifier", prefab.Identifier),
                    new XAttribute("name", prefab.Name),
                    new XAttribute("tags", FormatArray(prefab.Tags)),
                    new XAttribute("value", prefab.DefaultPrice?.Price ?? 0)
                );

                itemElement.Add(ParseRecipe(prefab));
                itemElement.Add(ParseDecon(prefab));
                itemElement.Add(ParseMedical(prefab));
                itemElement.Add(ParseWeapon(prefab));

                root.Add(itemElement);
            }

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = false,
                NewLineOnAttributes = false
            };

            using XmlWriter? writer = XmlWriter.Create("spreadsheetdata.xml", settings);
            doc.SaveSafe(writer);
        }

        private static XElement ParseRecipe(ItemPrefab prefab)
        {
            FabricationRecipe? recipe = prefab.FabricationRecipes.FirstOrDefault();

            List<ItemPrefab> ingredients = recipe?.RequiredItems.SelectMany(ri => ri.ItemPrefabs).Distinct().ToList() ?? new List<ItemPrefab>();
            Skill? skill = recipe?.RequiredSkills.FirstOrDefault();

            return new XElement("Recipe",
                new XAttribute("amount", recipe?.Amount ?? 0),
                new XAttribute("time", recipe?.RequiredTime ?? 0),
                new XAttribute("skillname", skill?.Identifier ?? ""),
                new XAttribute("skillamount", (int?) skill?.Level ?? 0),
                new XAttribute("ingredients", FormatArray(ingredients.Select(ip => ip.Name))),
                new XAttribute("values", FormatArray(ingredients.Select(ip => ip.DefaultPrice?.Price ?? 0)))
            );
        }

        private static XElement ParseDecon(ItemPrefab prefab)
        {
            List<ItemPrefab> deconOutput = prefab.DeconstructItems.Select(item => ItemPrefab.Find(null, item.ItemIdentifier)).Where(outputPrefab => outputPrefab != null).ToList();
            return new XElement("Deconstruct",
                new XAttribute("time", prefab.DeconstructTime),
                new XAttribute("outputs", FormatArray(deconOutput.Select(ip => ip.Name))),
                new XAttribute("values", FormatArray(deconOutput.Select(ip => ip.DefaultPrice?.Price ?? 0)))
            );
        }

        private static XElement ParseMedical(ItemPrefab prefab)
        {
            XElement? itemMeleeWeapon = prefab.ConfigElement.GetChildElement(nameof(MeleeWeapon));
            // affliction, amount, duration
            List<Tuple<string, float, float>> onSuccessAfflictions = new List<Tuple<string, float, float>>();
            List<Tuple<string, float, float>> onFailureAfflictions = new List<Tuple<string, float, float>>();
            int medicalRequiredSkill = 0;
            if (itemMeleeWeapon != null)
            {
                List<StatusEffect> statusEffects = new List<StatusEffect>();
                foreach (XElement subElement in itemMeleeWeapon.Elements())
                {
                    string name = subElement.Name.ToString();
                    if (name.Equals(nameof(StatusEffect), StringComparison.OrdinalIgnoreCase))
                    {
                        StatusEffect statusEffect = StatusEffect.Load(subElement, debugIdentifier);
                        if (statusEffect == null || !statusEffect.HasTag("medical")) { continue; }

                        statusEffects.Add(statusEffect);
                    }
                    else if (IsRequiredSkill(subElement, out Skill? skill) && skill != null)
                    {
                        medicalRequiredSkill = (int) skill.Level;
                    }
                }

                List<StatusEffect> successEffects = statusEffects.Where(se => se.type == ActionType.OnUse).ToList();
                List<StatusEffect> failureEffects = statusEffects.Where(se => se.type == ActionType.OnFailure).ToList();

                foreach (StatusEffect statusEffect in successEffects)
                {
                    float duration = statusEffect.Duration;
                    onSuccessAfflictions.AddRange(statusEffect.ReduceAffliction.Select(pair => Tuple.Create(GetAfflictionName(pair.affliction), -pair.amount, duration)));
                    onSuccessAfflictions.AddRange(statusEffect.Afflictions.Select(affliction => Tuple.Create(affliction.Prefab.Name, affliction.NonClampedStrength, duration)));
                }

                foreach (StatusEffect statusEffect in failureEffects)
                {
                    float duration = statusEffect.Duration;
                    onFailureAfflictions.AddRange(statusEffect.ReduceAffliction.Select(pair => Tuple.Create(GetAfflictionName(pair.affliction), -pair.amount, duration)));
                    onFailureAfflictions.AddRange(statusEffect.Afflictions.Select(affliction => Tuple.Create(affliction.Prefab.Name, affliction.NonClampedStrength, duration)));
                }
            }

            return new XElement("Medical",
                new XAttribute("skillamount", medicalRequiredSkill),
                new XAttribute("successafflictions", FormatArray(onSuccessAfflictions.Select(tpl => tpl.Item1))),
                new XAttribute("successamounts", FormatArray(onSuccessAfflictions.Select(tpl => FormatFloat(tpl.Item2)))),
                new XAttribute("successdurations", FormatArray(onSuccessAfflictions.Select(tpl => FormatFloat(tpl.Item3)))),
                new XAttribute("failureafflictions", FormatArray(onFailureAfflictions.Select(tpl => tpl.Item1))),
                new XAttribute("failureamounts", FormatArray(onFailureAfflictions.Select(tpl => FormatFloat(tpl.Item2)))),
                new XAttribute("failuredurations", FormatArray(onFailureAfflictions.Select(tpl => FormatFloat(tpl.Item3))))
            );
        }

        private static XElement ParseWeapon(ItemPrefab prefab)
        {
            float stun = 0;
            bool isAoE = false;
            float? structDamage = null;
            int skillRequirement = 0;

            // affliction, amount
            List<Tuple<string, float>> damages = new List<Tuple<string, float>>();

            string[] validNames = { nameof(Projectile), nameof(MeleeWeapon), nameof(RepairTool), nameof(ItemComponent), nameof(RangedWeapon) };
            foreach (XElement icElement in prefab.ConfigElement.Elements())
            {
                string icName = icElement.Name.ToString();
                if (!validNames.Any(name => icName.Equals(name, StringComparison.OrdinalIgnoreCase))) { continue; }

                foreach (XElement icChildElement in icElement.Elements())
                {
                    string name = icChildElement.Name.ToString();
                    if (IsRequiredSkill(icChildElement, out Skill? skill) && skill != null)
                    {
                        skillRequirement = (int) skill.Level;
                    }
                    else if (name.Equals(nameof(Attack), StringComparison.OrdinalIgnoreCase))
                    {
                        ParseAttack(new Attack(icChildElement, debugIdentifier));
                    }
                    else if (name.Equals(nameof(Explosion), StringComparison.OrdinalIgnoreCase))
                    {
                        ParseExplosion(new[] { new Explosion(icChildElement, debugIdentifier) });
                    }
                    else if (name.Equals(nameof(StatusEffect), StringComparison.OrdinalIgnoreCase))
                    {
                        ParseStatusEffect(new[] { StatusEffect.Load(icChildElement, debugIdentifier) });
                    }

                    void ParseStatusEffect(IEnumerable<StatusEffect> statusEffects)
                    {
                        foreach (StatusEffect effect in statusEffects)
                        {
                            if (effect.HasTargetType(StatusEffect.TargetType.Character)) { continue; }

                            ParseAfflictions(effect.Afflictions);
                            ParseExplosion(effect.Explosions);
                        }
                    }

                    void ParseExplosion(IEnumerable<Explosion> explosions)
                    {
                        foreach (Explosion explosion in explosions)
                        {
                            isAoE = true;
                            ParseAttack(explosion.Attack);
                            ParseStatusEffect(explosion.Attack.StatusEffects);
                        }
                    }

                    void ParseAttack(Attack attack)
                    {
                        structDamage ??= attack.StructureDamage;
                        ParseAfflictions(attack.Afflictions.Keys);
                        ParseStatusEffect(attack.StatusEffects);
                    }

                    void ParseAfflictions(IEnumerable<Affliction> afflictions)
                    {
                        foreach (Affliction affliction in afflictions)
                        {
                            // Exclude stuns
                            if (affliction.Prefab == AfflictionPrefab.Stun)
                            {
                                stun += affliction.NonClampedStrength;
                                continue;
                            }

                            damages.Add(Tuple.Create(affliction.Prefab.Name, affliction.NonClampedStrength));
                        }
                    }
                }
            }

            return new XElement("Weapon",
                new XAttribute("damagenames", FormatArray(damages.Select(tpl => tpl.Item1))),
                new XAttribute("damageamounts", FormatArray(damages.Select(tpl => FormatFloat(tpl.Item2)))),
                new XAttribute("isaoe", isAoE),
                new XAttribute("structuredamage", structDamage ?? 0),
                new XAttribute("stun", FormatFloat(stun)),
                new XAttribute("skillrequirement", skillRequirement)
            );
        }

        private static string GetAfflictionName(string identifier)
        {
            return AfflictionPrefab.Prefabs.Find(prefab => prefab.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))?.Name ?? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(identifier.ToLower());
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatArray<T>(IEnumerable<T> array)
        {
            return string.Join(separator, array);
        }

        private static bool IsRequiredSkill(XElement element, out Skill? skill)
        {
            string name = element.Name.ToString();
            bool isSkill = name.Equals("RequiredSkill", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("RequiredSkills", StringComparison.OrdinalIgnoreCase);

            if (isSkill)
            {
                string identifier = element.GetAttributeString(nameof(Skill.Identifier).ToLowerInvariant(), string.Empty);
                float level = element.GetAttributeFloat(nameof(Skill.Level).ToLowerInvariant(), 0f);
                skill = new Skill(identifier, level);
            }
            else
            {
                skill = null;
            }

            return isSkill;
        }
    }
}