using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent, IServerSerializable
    {
        private readonly LocalizedString materialName;

        private Character targetCharacter;
        private AfflictionPrefab selectedEffect, selectedTaintedEffect;

        [Serialize("", IsPropertySaveable.Yes)]
        public string Effect
        {
            get;
            set;
        }

        [Serialize("geneticmaterialdebuff", IsPropertySaveable.Yes)]
        public Identifier TaintedEffect
        {
            get;
            set;
        }

        private bool tainted;
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool Tainted
        {
            get { return tainted; }
            private set
            {
                if (!value) { return; }
                tainted = true;
                item.AllowDeconstruct = false;
                if (!TaintedEffect.IsEmpty)
                {
                    selectedTaintedEffect = AfflictionPrefab.Prefabs.Where(a =>
                        a.Identifier == TaintedEffect ||
                        a.AfflictionType == TaintedEffect).GetRandomUnsynced();
                }
            }
        }

        //only for saving the selected tainted effect
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier SelectedTaintedEffect
        {
            get { return selectedTaintedEffect?.Identifier ?? Identifier.Empty; }
            private set
            {
                selectedTaintedEffect = !value.IsEmpty ? AfflictionPrefab.Prefabs.Find(a => a.Identifier == value) : null;
            }
        }

        public GeneticMaterial(Item item, ContentXElement element)
            : base(item, element)
        {
            string nameId = element.GetAttributeString("nameidentifier", "");
            if (!string.IsNullOrEmpty(nameId))
            {
                materialName = TextManager.Get(nameId);
            }
            if (!string.IsNullOrEmpty(Effect))
            {
                selectedEffect = AfflictionPrefab.Prefabs.Where(a =>
                    a.Identifier == Effect ||
                    a.AfflictionType == Effect).GetRandomUnsynced();
            }
        }

        [Serialize(3.0f, IsPropertySaveable.No)]
        public float ConditionIncreaseOnCombineMin  { get; set; }

        [Serialize(8.0f, IsPropertySaveable.No)]
        public float ConditionIncreaseOnCombineMax { get; set; }

        public bool CanBeCombinedWith(GeneticMaterial otherGeneticMaterial)
        {
            return !tainted && otherGeneticMaterial != null && !otherGeneticMaterial.tainted && item.AllowDeconstruct && otherGeneticMaterial.item.AllowDeconstruct;
        }

        public override void Equip(Character character)
        {
            if (character == null) { return; }
            IsActive = true;

            if (targetCharacter != null) { return; }

            if (selectedEffect != null)
            {
                targetCharacter = character;
                ApplyStatusEffects(ActionType.OnWearing, 1.0f);
                float selectedEffectStrength = GetCombinedEffectStrength();
                character.CharacterHealth.ApplyAffliction(null, selectedEffect.Instantiate(selectedEffectStrength));
                var affliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                if (affliction != null) { affliction.Strength = selectedEffectStrength; }
#if SERVER
                item.CreateServerEvent(this);
#endif      
            }
            if (tainted && selectedTaintedEffect != null)
            {
                float selectedTaintedEffectStrength = GetCombinedTaintedEffectStrength();
                character.CharacterHealth.ApplyAffliction(null, selectedTaintedEffect.Instantiate(selectedTaintedEffectStrength));
                var affliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedTaintedEffect);
                if (affliction != null) { affliction.Strength = selectedTaintedEffectStrength; }
                targetCharacter = character;
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
            foreach (Item containedItem in item.ContainedItems)
            {
                containedItem.GetComponent<GeneticMaterial>()?.Equip(character);
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            if (targetCharacter != null)
            {
                var rootContainer = item.GetRootContainer();
                if (!targetCharacter.HasEquippedItem(item) && 
                    (rootContainer == null || !targetCharacter.HasEquippedItem(rootContainer) || !targetCharacter.Inventory.IsInLimbSlot(rootContainer, InvSlotType.HealthInterface)))
                {
                    item.ApplyStatusEffects(ActionType.OnSevered, 1.0f, targetCharacter);
                    IsActive = false;

                    var affliction = targetCharacter.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                    if (affliction != null) { affliction.Strength = GetCombinedEffectStrength(); }
                    var taintedAffliction = targetCharacter.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedTaintedEffect);
                    if (taintedAffliction != null) { taintedAffliction.Strength = GetCombinedTaintedEffectStrength(); }

                    targetCharacter = null;
                }
            }
        }

        public bool Combine(GeneticMaterial otherGeneticMaterial, Character user)
        {
            if (!CanBeCombinedWith(otherGeneticMaterial)) { return false; }

            float conditionIncrease = Rand.Range(ConditionIncreaseOnCombineMin, ConditionIncreaseOnCombineMax);
            conditionIncrease += user?.GetStatValue(StatTypes.GeneticMaterialRefineBonus) ?? 0.0f;
            if (item.Prefab == otherGeneticMaterial.item.Prefab)
            {
                item.Condition = Math.Max(item.Condition, otherGeneticMaterial.item.Condition) + conditionIncrease;
                float taintedProbability = GetTaintedProbabilityOnRefine(user);
                if (taintedProbability >= Rand.Range(0.0f, 1.0f))
                {
                    MakeTainted();
                }
                return true;
            }
            else
            {
                item.Condition = otherGeneticMaterial.Item.Condition =
                    (item.Condition + otherGeneticMaterial.Item.Condition) / 2.0f + conditionIncrease;
                item.OwnInventory?.TryPutItem(otherGeneticMaterial.Item, user: null);
                item.AllowDeconstruct = false;
                otherGeneticMaterial.Item.AllowDeconstruct = false;
                if (GetTaintedProbabilityOnCombine(user) >= Rand.Range(0.0f, 1.0f))
                {
                    MakeTainted();
                }
                return false;
            }
        }

        private float GetCombinedEffectStrength()
        {
            float effectStrength = 0.0f;
            foreach (Item otherItem in targetCharacter.Inventory.FindAllItems(recursive: true))
            {
                var geneticMaterial = otherItem.GetComponent<GeneticMaterial>();
                if (geneticMaterial == null || !geneticMaterial.IsActive) { continue; }
                if (geneticMaterial.selectedEffect == selectedEffect)
                {
                    effectStrength += otherItem.ConditionPercentage / 100.0f * selectedEffect.MaxStrength;
                }
            }
            return effectStrength;
        }

        private float GetCombinedTaintedEffectStrength()
        {
            float taintedEffectStrength = 0.0f;
            foreach (Item otherItem in targetCharacter.Inventory.FindAllItems(recursive: true))
            {
                var geneticMaterial = otherItem.GetComponent<GeneticMaterial>();
                if (geneticMaterial == null || !geneticMaterial.IsActive) { continue; }
                if (selectedTaintedEffect != null && geneticMaterial.selectedTaintedEffect == selectedTaintedEffect)
                {
                    taintedEffectStrength += otherItem.ConditionPercentage / 100.0f * selectedTaintedEffect.MaxStrength;
                }
            }
            return taintedEffectStrength;
        }

        private float GetTaintedProbabilityOnRefine(Character user)
        {
            if (user == null) { return 1.0f; }
            float probability = MathHelper.Lerp(0.0f, 0.99f, item.Condition / 100.0f);
            probability *= MathHelper.Lerp(1.0f, 0.25f, DegreeOfSuccess(user));
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        private float GetTaintedProbabilityOnCombine(Character user)
        {
            if (user == null) { return 1.0f; }
            float probability = 1.0f - user.GetStatValue(StatTypes.GeneticMaterialTaintedProbabilityReductionOnCombine);
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        private void MakeTainted()
        {
            if (GameMain.NetworkMember?.IsClient ?? false) { return; }
            Tainted = true;
#if SERVER
            item.CreateServerEvent(this);
#endif            
        }

        public static LocalizedString TryCreateName(ItemPrefab prefab, XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == nameof(GeneticMaterial))
                {
                    Identifier nameId = subElement.GetAttributeIdentifier("nameidentifier", "");
                    if (!nameId.IsEmpty)
                    {
                        return prefab.Name.Replace("[type]", TextManager.Get(nameId).Fallback(nameId.Value));
                    }
                }
            }
            return prefab.Name;
        }
    }
}
