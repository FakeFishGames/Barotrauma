using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent
    {
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float TooltipValueMin { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float TooltipValueMax { get; set; }

        public override void AddTooltipInfo(ref LocalizedString name, ref LocalizedString description)
        {
            if (!materialName.IsNullOrEmpty() && item.ContainedItems.Count() > 0)
            {
                LocalizedString mergedMaterialName = materialName;
                foreach (Item containedItem in item.ContainedItems)
                {
                    var containedMaterial = containedItem.GetComponent<GeneticMaterial>();
                    if (containedMaterial == null) { continue; }
                    mergedMaterialName += ", " + containedMaterial.materialName;
                }
                name = name.Replace(materialName, mergedMaterialName);
            }

            if (Tainted)
            {
                name = TextManager.GetWithVariable("entityname.taintedgeneticmaterial", "[geneticmaterialname]", name);
            }

            if (TextManager.ContainsTag("entitydescription." + Item.Prefab.Identifier))
            {
                int value = (int)MathHelper.Lerp(TooltipValueMin, TooltipValueMax, item.ConditionPercentage / 100.0f);
                description = TextManager.GetWithVariable("entitydescription." + Item.Prefab.Identifier, "[value]", value.ToString());
            }
            foreach (Item containedItem in item.ContainedItems)
            {
                var containedGeneticMaterial = containedItem.GetComponent<GeneticMaterial>();
                if (containedGeneticMaterial == null) { continue; }
                LocalizedString _ = string.Empty;
                LocalizedString containedDescription = containedItem.Description;
                containedGeneticMaterial.AddTooltipInfo(ref _, ref containedDescription);
                if (!containedDescription.IsNullOrEmpty())
                {
                    description += '\n' + containedDescription;
                }
            }
        }

        public void ModifyDeconstructInfo(Deconstructor deconstructor, ref LocalizedString buttonText, ref LocalizedString infoText)
        {
            if (deconstructor.InputContainer.Inventory.AllItems.Count() == 2)
            {
                if (!deconstructor.InputContainer.Inventory.AllItems.All(it => it.Prefab == item.Prefab))
                {
                    buttonText = TextManager.Get("researchstation.combine");
                    infoText = TextManager.Get("researchstation.combine.infotext");
                }
                else
                {
                    buttonText = TextManager.Get("researchstation.refine");
                    int taintedProbability = (int)(GetTaintedProbabilityOnRefine(Character.Controlled) * 100);
                    infoText = TextManager.GetWithVariable("researchstation.refine.infotext", "[taintedprobability]", taintedProbability.ToString());
                }
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            Tainted = msg.ReadBoolean();
            if (Tainted)
            {
                uint selectedTaintedEffectId = msg.ReadUInt32();
                selectedTaintedEffect = AfflictionPrefab.Prefabs.Find(a => a.UintIdentifier == selectedTaintedEffectId);
            }
            else
            {
                uint selectedEffectId = msg.ReadUInt32();
                selectedEffect = AfflictionPrefab.Prefabs.Find(a => a.UintIdentifier == selectedEffectId);
            }
        }
    }
}
