using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    public class Vent : ItemComponent
    {
        private float oxygenFlow;

        private float oxygenFlowFromOtherVents;

        [Serialize(defaultValue: 0.0f, IsPropertySaveable.No)]
        public float MaxOxygenFlowToOtherVents
        {
            get;
            set;
        }

        [Serialize(defaultValue: true, IsPropertySaveable.No, description: "Should oxygen from the vent increase the oxygen level of the hull it is in?")]
        public bool ShouldOxygenFlowToHull
        {
            get;
            set;
        }

        [Serialize(defaultValue: 0.0f, IsPropertySaveable.No, description: "Makes the vent pull a fixed amount of oxygen from linked vents, regardless of this vent's hull oxygen level.")]
        public float FixedOxygenConsumptionFromOtherVents
        {
            get;
            set;
        }

        [Editable, Serialize(defaultValue: "", IsPropertySaveable.Yes, description: "Automatically links any vents that have the specified tag or id when this item spawns. Note that any previous links are cleared, meaning that manually placed links done through the submarine editor are lost.")]
        public string LinkWithOtherVentsIdOrTag
        {
            get;
            set;
        }

        /// <summary>
        /// Can be used by status effects to temporarily disable this vent.
        /// </summary>
        public float OxygenFlowBlockedTime { get; set; }

        /// <summary>
        /// How much the vent is currently increasing the oxygen level in the current hull per second. Does not take <see cref="oxygenFlowFromOtherVents">oxygen flow from other vents</see> into account.
        /// </summary>
        public float OxygenFlow
        {
            get { return oxygenFlow; }
            set { oxygenFlow = Math.Max(value, 0.0f); }
        }

        /// <summary>
        /// How much the vent is currently increasing the oxygen level in the current hull per second, also taking into account <see cref="oxygenFlowFromOtherVents">oxygen flow from other vents</see>. 
        /// </summary>
        public float TotalOxygenFlow => OxygenFlowBlockedTime > 0f ? 0f : oxygenFlow + oxygenFlowFromOtherVents;

        /// <summary>
        /// A smoother version of <see cref="TotalOxygenFlow"/>. Slowly goes back to zero when there's no oxygen flow.
        /// Intended to be used for sound and animation purposes, so a vent that's quickly turning on and off doesn't keep "jittering" and repeatedly starting and stopping the sound.
        /// </summary>
        public float SmoothedTotalOxygenFlow { get; private set; }

        private bool hasAutomaticallyLinkedToVents = false;

        private double LastOxygenFlowTime;

        public Vent(Item item, ContentXElement element) : base(item, element)  
        {
            IsActive = true;
        }

        private void LinkToVents()
        {
            if (hasAutomaticallyLinkedToVents) { return; }
            if (Screen.Selected is { IsEditor: true }) { return; }
            if (Item.Submarine == null) { return; }
            if (LinkWithOtherVentsIdOrTag.IsNullOrEmpty()) { return; }

            hasAutomaticallyLinkedToVents = true;

            // Remove previous links
            foreach (Item linked in item.linkedTo.ToList())
            {
                if (linked.GetComponent<Vent>() is { hasAutomaticallyLinkedToVents: true })
                {
                    // Don't remove automatic links from other vents, otherwise we'll end up discarding their links depending on the load order
                    continue;
                }

                linked.linkedTo.Remove(Item);
                Item.linkedTo.Remove(linked);
            }

            HashSet<Identifier> tagsOrIdentifiers = LinkWithOtherVentsIdOrTag.ToIdentifiers().ToHashSet();

            foreach (Item otherVent in 
                Item.Submarine.GetItems(alsoFromConnectedSubs: true)
                .Where(item => item.HasIdentifierOrTags(tagsOrIdentifiers)))
            {
                if (otherVent == Item) { continue; }

                Item.linkedTo.Add(otherVent);
                otherVent.linkedTo.Add(Item);
            }
        }

        public override void OnMapLoaded()
        {
            LinkToVents();
        }

        public override void Update(float deltaTime, Camera cam)
        {
            OxygenFlowBlockedTime -= deltaTime;

            if (item.CurrentHull == null || item.InWater) { return; }

            if (!hasAutomaticallyLinkedToVents) { LinkToVents(); }

            if (TotalOxygenFlow > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime);
            }

            //calculate how much oxygen is flowing from other vents
            oxygenFlowFromOtherVents = 0.0f;
            foreach (var linked in item.linkedTo)
            {
                if (linked is not Item linkedItem ||
                    linkedItem.CurrentHull == null ||
                    linkedItem.GetComponent<Vent>() is not Vent linkedVent ||
                    linkedVent.MaxOxygenFlowToOtherVents <= 0.0f)
                {
                    continue;
                }

                float oxygenDelta = 0.0f;

                if (FixedOxygenConsumptionFromOtherVents > 0.0f)
                {
                    if (linkedItem.CurrentHull.Oxygen > FixedOxygenConsumptionFromOtherVents * deltaTime)
                    {
                        oxygenDelta = FixedOxygenConsumptionFromOtherVents;
                    }
                }
                else if (ShouldOxygenFlowToHull)
                {
                    if (linkedItem.CurrentHull.OxygenPercentage > item.CurrentHull.OxygenPercentage)
                    {
                        oxygenDelta = linkedItem.CurrentHull.Oxygen;
                    }
                }

                oxygenDelta = Math.Min(linkedVent.MaxOxygenFlowToOtherVents, oxygenDelta);

                if (oxygenDelta > 0.0f)
                {
                    // Decrease intake vent's hull oxygen
                    linkedItem.CurrentHull.Oxygen -= oxygenDelta * deltaTime;
                    oxygenFlowFromOtherVents += oxygenDelta;
                }
            }

            if (TotalOxygenFlow > 0.0f)
            {
                SmoothedTotalOxygenFlow = TotalOxygenFlow;
                LastOxygenFlowTime = Timing.TotalTime;
            }
            //start lerping smoothed flow down if there hasn't been any oxygen flow within 1 second
            else if (Timing.TotalTime > LastOxygenFlowTime + 1.0)
            {
                if (SmoothedTotalOxygenFlow > 1.0f)
                {
                    //above one, lerp down so we can get the value down quickly regardless of how large it is
                    SmoothedTotalOxygenFlow = MathHelper.Lerp(SmoothedTotalOxygenFlow, 0.0f, deltaTime * 10.0f);
                }
                else
                {
                    //below one, just reduce down to 0 within a second
                    SmoothedTotalOxygenFlow = Math.Max(SmoothedTotalOxygenFlow - deltaTime, 0.0f);
                }
            }

            if (ShouldOxygenFlowToHull)
            {
                item.CurrentHull.Oxygen += TotalOxygenFlow * deltaTime;
            }

            //reduce the flow towards zero, so the vent stops supplying oxygen if it doesn't receive it from somewhere
            oxygenFlow = Math.Max(oxygenFlow - deltaTime * 1000.0f, 0.0f);
        }
    }
}
