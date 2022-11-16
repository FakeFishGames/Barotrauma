using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class TriggerAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the first entity that will be used for trigger checks.")]
        public Identifier Target1Tag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the second entity that will be used for trigger checks.")]
        public Identifier Target2Tag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, the first target has to be within an outpost module of this type.")]
        public Identifier TargetModuleType { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the first entity when the trigger check succeeds.")]
        public Identifier ApplyToTarget1 { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the second entity when the trigger check succeeds.")]
        public Identifier ApplyToTarget2 { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Range both entities must be within to activate the trigger.")]
        public float Radius { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, characters who are being targeted by some enemy cannot trigger the action.")]
        public bool DisableInCombat { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, dead/unconscious characters cannot trigger the action.")]
        public bool DisableIfTargetIncapacitated { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, one target must interact with the other to trigger the action.")]
        public bool WaitForInteraction { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, the action can be triggered by interacting with any matching target (not just the 1st one).")]
        public bool AllowMultipleTargets { get; set; }

        private float distance;

        public TriggerAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private bool isFinished = false;
        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            ResetTargetIcons();
            isRunning = false;
            isFinished = false;
        }

        public bool isRunning = false;

        private readonly List<Either<Character, Item>> npcsOrItems = new List<Either<Character, Item>>();

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            isRunning = true;

            var targets1 = ParentEvent.GetTargets(Target1Tag);
            if (!targets1.Any()) { return; }
            
            foreach (Entity e1 in targets1)
            {
                if (DisableInCombat && IsInCombat(e1)) { continue; }
                if (DisableIfTargetIncapacitated && e1 is Character character1 && (character1.IsDead || character1.IsIncapacitated)) { continue; }
                if (!TargetModuleType.IsEmpty)
                {
                    if (IsCloseEnoughToHull(e1, out Hull hull))
                    {
                        Trigger(e1, hull);
                        return;
                    }
                    continue;
                }

                var targets2 = ParentEvent.GetTargets(Target2Tag);
                
                foreach (Entity e2 in targets2)
                {
                    if (e1 == e2) { continue; }
                    if (DisableInCombat && IsInCombat(e2)) { continue; }
                    if (DisableIfTargetIncapacitated && e2 is Character character2 && (character2.IsDead || character2.IsIncapacitated)) { continue; }

                    if (WaitForInteraction)
                    {
                        Character player = null;
                        Character npc = null;
                        Item item = null;
                        if (e1 is Character char1)
                        {
                            if (char1.IsBot) 
                            { 
                                npc ??= char1; 
                            }
                            else 
                            { 
                                player = char1; 
                            }
                        }
                        else
                        {
                            item ??= e1 as Item;
                        }
                        if (e2 is Character char2)
                        {
                            if (char2.IsBot) 
                            { 
                                npc ??= char2; 
                            }
                            else 
                            {
                                player = char2; 
                            }
                        }
                        else
                        {
                            item ??= e2 as Item;
                        }

                        if (player != null)
                        {
                            if (npc != null)
                            {
                                if (npc.CampaignInteractionType != CampaignMode.InteractionType.Examine)
                                {
                                    if (!npcsOrItems.Any(n => n.TryGet(out Character npc2) && npc2 == npc)) 
                                    {
                                        npcsOrItems.Add(npc);
                                    } 
                                    npc.CampaignInteractionType = CampaignMode.InteractionType.Examine;
                                    npc.RequireConsciousnessForCustomInteract = DisableIfTargetIncapacitated;
#if CLIENT
                                    npc.SetCustomInteract(
                                        (speaker, player) => { if (e1 == speaker) { Trigger(speaker, player); } else { Trigger(player, speaker); } },
                                        TextManager.GetWithVariable("CampaignInteraction.Examine", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)));
#else
                                    npc.SetCustomInteract(
                                        (speaker, player) => { if (e1 == speaker) { Trigger(speaker, player); } else { Trigger(player, speaker); } }, 
                                        TextManager.Get("CampaignInteraction.Talk"));
                                    GameMain.NetworkMember.CreateEntityEvent(npc, new Character.AssignCampaignInteractionEventData());
#endif
                                }
                                if (!AllowMultipleTargets) { return; }
                            }
                            else if (item != null)
                            {
                                if (!npcsOrItems.Any(n => n.TryGet(out Item item2) && item2 == item))
                                {
                                    npcsOrItems.Add(item);
                                }
                                item.CampaignInteractionType = CampaignMode.InteractionType.Examine;
                                if (player.SelectedItem == item ||
                                    player.SelectedSecondaryItem == item ||
                                    (player.Inventory != null && player.Inventory.Contains(item)) ||
                                    (player.FocusedItem == item && player.IsKeyHit(InputType.Use)))
                                {
                                    Trigger(e1, e2);
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        Vector2 pos1 = e1.WorldPosition;
                        Vector2 pos2 = e2.WorldPosition;
                        distance = Vector2.Distance(pos1, pos2);
                        if (((e1 is MapEntity m1) && Submarine.RectContains(m1.WorldRect, pos2)) ||
                            ((e2 is MapEntity m2) && Submarine.RectContains(m2.WorldRect, pos1)) ||
                            Vector2.DistanceSquared(pos1, pos2) < Radius * Radius)
                        {
                            Trigger(e1, e2);
                            return;
                        }
                    }
                }
            }            
        }

        private void ResetTargetIcons()
        {
            foreach (var npcOrItem in npcsOrItems)
            {
                if (npcOrItem.TryGet(out Character npc))
                {
                    npc.CampaignInteractionType = CampaignMode.InteractionType.None;
                    npc.SetCustomInteract(null, null);
                    npc.RequireConsciousnessForCustomInteract = true;
    #if SERVER
                    GameMain.NetworkMember.CreateEntityEvent(npc, new Character.AssignCampaignInteractionEventData());
    #endif
                }
                else if (npcOrItem.TryGet(out Item item))
                {
                    item.CampaignInteractionType = CampaignMode.InteractionType.None;
                }
            }
        }

        private bool IsCloseEnoughToHull(Entity e, out Hull hull)
        {
            hull = null;
            if (Radius <= 0)
            {
                if (e is Character character && character.CurrentHull != null && character.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = character.CurrentHull;
                    return true;
                }
                else if (e is Item item && item.CurrentHull != null && item.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = item.CurrentHull;
                    return true;
                }
                return false;
            }
            else
            {
                foreach (Hull potentialHull in Hull.HullList)
                {
                    if (!potentialHull.OutpostModuleTags.Contains(TargetModuleType)) { continue; }

                    Rectangle hullRect = potentialHull.WorldRect;
                    hullRect.Inflate(Radius, Radius);
                    if (Submarine.RectContains(hullRect, e.WorldPosition))
                    {
                        hull = potentialHull;
                        return true;
                    }
                }
                return false;
            }
        }

        private bool IsInCombat(Entity entity)
        {
            if (!(entity is Character character)) { return false; }
            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead || c.Removed || c.IsIncapacitated || !c.Enabled) { continue; }
                if (c.IsBot && c.AIController is HumanAIController humanAi)
                {
                    if (humanAi.ObjectiveManager.CurrentObjective is AIObjectiveCombat combatObjective &&
                        combatObjective.Enemy == character)
                    {
                        return true;
                    }
                }
                else if (c.AIController is EnemyAIController enemyAI && (enemyAI.State == AIState.Aggressive || enemyAI.State == AIState.Attack))
                {
                    if (enemyAI.SelectedAiTarget?.Entity == character || c.CurrentHull == character.CurrentHull)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void Trigger(Entity entity1, Entity entity2)
        {
            ResetTargetIcons();
            if (!ApplyToTarget1.IsEmpty)
            {
                ParentEvent.AddTarget(ApplyToTarget1, entity1);
            }
            if (!ApplyToTarget2.IsEmpty)
            {
                ParentEvent.AddTarget(ApplyToTarget2, entity2);
            }

            isRunning = false;
            isFinished = true;
        }

        public override string ToDebugString()
        {
            if (TargetModuleType.IsEmpty)
            {
                return
                    $"{ToolBox.GetDebugSymbol(isFinished, isRunning)} {nameof(TriggerAction)} -> (" +
                    (WaitForInteraction ?
                        $"Selected non-player target: {(npcsOrItems?.ToString() ?? "<null>").ColorizeObject()}, " :
                        $"Distance: {((int)distance).ColorizeObject()}, ") +
                    $"Radius: {Radius.ColorizeObject()}, " +
                    $"TargetTags: {Target1Tag.ColorizeObject()}, " +
                    $"{Target2Tag.ColorizeObject()})";
            }
            else
            {
                return $"{ToolBox.GetDebugSymbol(isFinished, isRunning)} {nameof(TriggerAction)} -> (TargetTags: {Target1Tag.ColorizeObject()}, {TargetModuleType.ColorizeObject()})";
            }
        }
    }
}