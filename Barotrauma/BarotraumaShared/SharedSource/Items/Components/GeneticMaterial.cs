using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent, IServerSerializable
    {
        private readonly LocalizedString materialName;

        private Character targetCharacter;
        private AfflictionPrefab selectedEffect, selectedTaintedEffect;

        [Serialize("", IsPropertySaveable.Yes)]
        public string Effect { get; set; }

        [Serialize("geneticmaterialdebuff", IsPropertySaveable.Yes, description: "Either the identifier or the type for the tainted effect prefab")]
        public Identifier TaintedEffect { get; set; }

        private bool tainted;

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool Tainted
        {
            get { return tainted; }
            set
            {
                tainted = value;
                if (tainted)
                {
                    if (!TaintedEffect.IsEmpty)
                    {
                        selectedTaintedEffect = AfflictionPrefab.Prefabs.Where(a =>
                            a.Identifier == TaintedEffect ||
                            a.AfflictionType == TaintedEffect).GetRandomUnsynced();
                    }
                }
                else
                {
                    if (targetCharacter != null)
                    {
                        var affliction = targetCharacter.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                        if (affliction != null)
                        {
                            affliction.Strength = 0;
                        }
                    }

                    selectedTaintedEffect = null;
                }
            }
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool SetTaintedOnDeath { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool CanBeUntainted { get; private set; }

        //only for saving the selected tainted effect
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier SelectedTaintedEffect
        {
            get { return selectedTaintedEffect?.Identifier ?? Identifier.Empty; }
            private set { selectedTaintedEffect = !value.IsEmpty ? AfflictionPrefab.Prefabs.Find(a => a.Identifier == value) : null; }
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

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float ConditionIncreaseOnCombineMin { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float ConditionIncreaseOnCombineMax { get; set; }

        [Serialize(3.0f, IsPropertySaveable.No, description: "When refining, min value for condition increase bonus based on the quality of the worse gene.")]
        public float ConditionIncreaseOnLowQualityCombine { get; set; }

        [Serialize(25.0f, IsPropertySaveable.No, description: "When refining, max value for condition increase bonus based on the quality of the worse gene.")]
        public float ConditionIncreaseOnHighQualityCombine { get; set; }
        
        private bool SharesTypeWith(GeneticMaterial otherGeneticMaterial)
        {
            return GetSharedTypeOrDefault(otherGeneticMaterial) != null;
        }

        private ItemPrefab GetSharedTypeOrDefault(GeneticMaterial otherGeneticMaterial)
        {
            if (otherGeneticMaterial == null) { return default; }

            return AllMaterialTypes.FirstOrDefault(materialType => otherGeneticMaterial.AllMaterialTypes.Contains(materialType));
        }

        private IEnumerable<ItemPrefab> AllMaterialTypes
        {
            get
            {
                yield return item.Prefab;

                if (IsCombined) { yield return NestedMaterial.item.Prefab; }
            }
        }

        private GeneticMaterial NestedMaterial
        {
            get
            {
                if (item == null || item.OwnInventory == null) { return null; }

                var nestedItemWithGeneticMaterial = item.OwnInventory.AllItems.FirstOrDefault(it => it.GetComponent<GeneticMaterial>() != null);
                if (nestedItemWithGeneticMaterial == null) { return null; }

                return nestedItemWithGeneticMaterial.GetComponent<GeneticMaterial>();
            }
        }

        private bool IsCombined
        {
            get
            {
                if (NestedMaterial != null) { return true; }

                // check if this is the nested material
                if (item.ParentInventory != null && 
                    item.ParentInventory.Owner is Item parentItem && 
                    parentItem.GetComponent<GeneticMaterial>()?.NestedMaterial == this)
                {
                    return true;
                }

                return false;
            }
        }

        private CombineResult GetCombineRefineResult(GeneticMaterial otherGeneticMaterial)
        {
            if (otherGeneticMaterial == null)
            {
                return CombineResult.None;
            }

            // both are combined, no more processing is possible
            if (IsCombined && otherGeneticMaterial.IsCombined)
            {
                return CombineResult.None;
            }

            // neither is combined, can be either refined or combined
            if (!IsCombined && !otherGeneticMaterial.IsCombined)
            {
                return SharesTypeWith(otherGeneticMaterial) ? CombineResult.Refined : CombineResult.Combined;
            }

            // one is combined, if they share a type, they can be refined
            return SharesTypeWith(otherGeneticMaterial) ? CombineResult.Refined : CombineResult.None;
        }

        public bool CanBeCombinedWith(GeneticMaterial otherGeneticMaterial)
        {
            return GetCombineRefineResult(otherGeneticMaterial) != CombineResult.None;
        }

        public override void Equip(Character character)
        {
            if (character == null) { return; }

            IsActive = true;

            if (targetCharacter != null) { return; }

            if (selectedEffect != null)
            {
                targetCharacter = character;
                ApplyStatusEffects(ActionType.OnWearing, 1.0f, targetCharacter);
                float selectedEffectStrength = GetCombinedEffectStrength();
                character.CharacterHealth.ApplyAffliction(null, selectedEffect.Instantiate(selectedEffectStrength));
                var affliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                if (affliction != null)
                {
                    affliction.Strength = selectedEffectStrength;
                    //force strength to the correct value to bypass any clamping e.g. AfflictionHusk might be doing
                    affliction.SetStrength(selectedEffectStrength);
                }
#if SERVER
                item.CreateServerEvent(this);
#endif
            }

            if (tainted && selectedTaintedEffect != null)
            {
                float selectedTaintedEffectStrength = GetCombinedTaintedEffectStrength();
                character.CharacterHealth.ApplyAffliction(null, selectedTaintedEffect.Instantiate(selectedTaintedEffectStrength));
                var affliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedTaintedEffect);
                if (affliction != null)
                {
                    affliction.Strength = selectedTaintedEffectStrength;
                    //force strength to the correct value to bypass any clamping e.g. AfflictionHusk might be doing
                    affliction.SetStrength(selectedTaintedEffectStrength);
                }

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
                if (SetTaintedOnDeath && !tainted && 
                    targetCharacter.IsDead && targetCharacter.CauseOfDeath is not { Type: CauseOfDeathType.Disconnected })
                {
                    SetTainted(true);
                }

                var rootContainer = item.RootContainer;
                if (!targetCharacter.HasEquippedItem(item) &&
                    (rootContainer == null || !targetCharacter.HasEquippedItem(rootContainer) || !targetCharacter.Inventory.IsInLimbSlot(rootContainer, InvSlotType.HealthInterface)))
                {
                    item.ApplyStatusEffects(ActionType.OnSevered, 1.0f, targetCharacter);
                    //deactivate so the material is no longer updated or considered to be "in effect" in GetCombinedEffectStrength
                    IsActive = false;
                    var affliction = targetCharacter.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                    if (affliction != null)
                    {
                        affliction.Strength = GetCombinedEffectStrength();
                    }

                    var taintedAffliction = targetCharacter.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedTaintedEffect);
                    if (taintedAffliction != null)
                    {
                        taintedAffliction.Strength = GetCombinedTaintedEffectStrength();
                    }

                    targetCharacter = null;
                }
            }
        }

        public enum CombineResult
        {
            None,
            Refined,
            Combined
        }

        public CombineResult Combine(GeneticMaterial otherGeneticMaterial, Character user, out Item itemToDestroy)
        {
            var combineRefineResult = GetCombineRefineResult(otherGeneticMaterial);

            float randomQualityIncrease = Rand.Range(ConditionIncreaseOnCombineMin, ConditionIncreaseOnCombineMax);
            float talentIncrease = user?.GetStatValue(StatTypes.GeneticMaterialRefineBonus) ?? 0.0f;

            bool perfectQuality = item.IsFullCondition || otherGeneticMaterial.item.IsFullCondition;
            
            itemToDestroy = otherGeneticMaterial.item;
            
            if (combineRefineResult == CombineResult.Refined)
            {
                float maxQuality = Math.Max(item.Condition, otherGeneticMaterial.item.Condition);
                float minQuality = Math.Min(item.Condition, otherGeneticMaterial.item.Condition);
                bool oneIsCombined = IsCombined || otherGeneticMaterial.IsCombined;
                
                float minQualityProportionalIncreaseBonus = MathHelper.Lerp(ConditionIncreaseOnLowQualityCombine, ConditionIncreaseOnHighQualityCombine, Math.Clamp(minQuality / 80.0f, 0f, 1f));
                float totalQualityIncrease = minQualityProportionalIncreaseBonus + randomQualityIncrease + talentIncrease;
                if (oneIsCombined) { totalQualityIncrease /= 2f; }
                
                float newQuality = maxQuality + totalQualityIncrease;

                // the deconstructor wants to remove and delete the item for otherGeneticMaterial, 
                // we want to keep the type that's not being refined here, so we move around the items
                if (!IsCombined && otherGeneticMaterial.IsCombined)
                {
                    if (item.Prefab == otherGeneticMaterial.item.Prefab)
                    {
                        // main items share type, nest the non-shared item
                        item.OwnInventory?.TryPutItem(otherGeneticMaterial.NestedMaterial.item, user: null);
                    }
                    else
                    {
                        // the non-shared item is the main item in otherGeneticMaterial,
                        // we need to nest it...
                        item.OwnInventory?.TryPutItem(otherGeneticMaterial.item, user: null);
                        
                        // ...and get rid of the now triple-nested item inside it
                        var otherNestedItem = otherGeneticMaterial.NestedMaterial.item;
                        otherGeneticMaterial.item.OwnInventory?.RemoveItem(otherNestedItem);
                        itemToDestroy = otherNestedItem;
                    }
                }
                
                // note: the condition of combined items represents the quality of both materials,
                // and the condition of the nested item should equal that of the housing item
                item.Condition = newQuality;
                // this can become combined as a result of the item shuffling above
                if (IsCombined) { NestedMaterial.item.Condition = newQuality; }
                
                // if one item is 100% condition, remove taint when refining
                if (CanBeUntainted && perfectQuality)
                {
                    SetTainted(false, affectsNestedGene: true);
                }
                else if (GetTaintedProbabilityOnRefine(otherGeneticMaterial, user) >= Rand.Range(0.0f, 1.0f))
                {
                    SetTainted(true);
                }
            }
            else if (combineRefineResult == CombineResult.Combined)
            {
                float averageQuality = (item.Condition + otherGeneticMaterial.Item.Condition) / 2.0f;
                item.Condition = otherGeneticMaterial.Item.Condition = averageQuality + randomQualityIncrease + talentIncrease;
                item.OwnInventory?.TryPutItem(otherGeneticMaterial.Item, user: null);

                // if one item is 100% condition, remove taint when combining
                if (CanBeUntainted && perfectQuality)
                {
                    SetTainted(false, affectsNestedGene: true);
                }
                else if (GetTaintedProbabilityOnCombine(user) >= Rand.Range(0.0f, 1.0f))
                {
                    SetTainted(true);
                }
            }

            return combineRefineResult;
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

        private float GetTaintedProbabilityOnRefine(GeneticMaterial otherGeneticMaterial, Character user)
        {
            if (user == null) { return 1.0f; }

            float probability = MathHelper.Lerp(0.0f, 0.99f, Math.Max(item.Condition, otherGeneticMaterial.Item.Condition) / 100.0f);
            probability *= MathHelper.Lerp(1.0f, 0.25f, DegreeOfSuccess(user));
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        private static float GetTaintedProbabilityOnCombine(Character user)
        {
            if (user == null) { return 1.0f; }

            float probability = 1.0f - user.GetStatValue(StatTypes.GeneticMaterialTaintedProbabilityReductionOnCombine);
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        public void SetTainted(bool newValue, bool affectsNestedGene = false)
        {
            if (GameMain.NetworkMember?.IsClient ?? false) { return; }

            Tainted = newValue;
#if SERVER
            item.CreateServerEvent(this);
#endif

            if (affectsNestedGene && NestedMaterial != null)
            {
                NestedMaterial.SetTainted(newValue);
            }
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