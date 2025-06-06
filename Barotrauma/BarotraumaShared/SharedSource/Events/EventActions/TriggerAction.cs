﻿using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Waits for a player to trigger the action before continuing. Triggering can mean entering a specific trigger area, or interacting with a specific entity.
    /// </summary>
    class TriggerAction : EventAction
    {
        public enum TriggerType
        {
            Inside,
            Outside
        }

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

        [Serialize(TriggerType.Inside, IsPropertySaveable.Yes, description: "Determines if the targets must be inside or outside of the radius.")]
        public TriggerType Type { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Range to activate the trigger.")]
        public float Radius { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, characters who are being targeted by some enemy cannot trigger the action.")]
        public bool DisableInCombat { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, dead/unconscious characters cannot trigger the action.")]
        public bool DisableIfTargetIncapacitated { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, one target must interact with the other to trigger the action.")]
        public bool WaitForInteraction { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, the action can be triggered by interacting with any matching target (not just the 1st one).")]
        public bool AllowMultipleTargets { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true and using multiple targets, all targets must be inside/outside the radius.")]
        public bool CheckAllTargets { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, interacting with the target will make the character select it.")]
        public bool SelectOnTrigger { get; set; }

        private float distance;

        public TriggerAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
            if (element.GetAttribute(nameof(TagAction.IgnoreIncapacitatedCharacters)) != null)
            {
                DebugConsole.AddWarning(
                    $"Potential error in {nameof(TriggerAction)}, event \"{parentEvent.Prefab.Identifier}\": "+
                    $"{nameof(TagAction.IgnoreIncapacitatedCharacters)} is a property of {nameof(TagAction)}, did you mean {nameof(DisableIfTargetIncapacitated)}?",
                    contentPackage: element.ContentPackage);
            }
        }

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
        
        private readonly List<(Entity e1, Entity e2)> triggerers = new List<(Entity e1, Entity e2)>();

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            isRunning = true;

            var targets1 = ParentEvent.GetTargets(Target1Tag);
            if (!targets1.Any()) { return; }

            triggerers.Clear();
            foreach (Entity e1 in targets1)
            {
                if (DisableInCombat && IsInCombat(e1))
                {
                    if (CheckAllTargets)
                    {
                        return;
                    }
                    continue;
                }
                if (DisableIfTargetIncapacitated && e1 is Character character1 && (character1.IsDead || character1.IsIncapacitated))
                {
                    if (CheckAllTargets)
                    {
                        return;
                    }
                    continue;
                }
                if (!TargetModuleType.IsEmpty)
                {
                    if (!CheckAllTargets && CheckDistanceToHull(e1, out Hull hull))
                    {
                        Trigger(e1, hull);
                        return;
                    }
                    else if (CheckAllTargets)
                    {
                        if (CheckDistanceToHull(e1, out hull))
                        {
                            triggerers.Add((e1, hull));
                        }
                        else
                        {
                            return;
                        }
                    }
                    continue;
                }

                var targets2 = ParentEvent.GetTargets(Target2Tag);
                
                foreach (Entity e2 in targets2)
                {
                    if (e1 == e2)
                    {
                        continue;
                    }
                    if (DisableInCombat && IsInCombat(e2))
                    {
                        if (CheckAllTargets)
                        {
                            return;
                        }
                        continue;
                    }
                    if (DisableIfTargetIncapacitated && e2 is Character character2 && (character2.IsDead || character2.IsIncapacitated))
                    {
                        if (CheckAllTargets)
                        {
                            return;
                        }
                        continue;
                    }

                    if (WaitForInteraction)
                    {
                        Character player = null;
                        Character npc = null;
                        Item item = null;
                        if (e1 is Character char1)
                        {
                            if (char1.IsPlayer)
                            {
                                player = char1;
                            }
                            else
                            {
                                npc ??= char1;
                            }
                        }
                        else
                        {
                            item ??= e1 as Item;
                        }
                        if (e2 is Character char2)
                        {
                            if (char2.IsPlayer)
                            {
                                player = char2;
                            }
                            else
                            {
                                npc ??= char2;
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
                                if (!npcsOrItems.Any(n => n.TryGet(out Character npc2) && npc2 == npc)) 
                                {
                                    npcsOrItems.Add(npc);
                                } 
                                if (npc.CampaignInteractionType == CampaignMode.InteractionType.Talk)
                                {
                                    //if the NPC has a conversation available, don't assign the trigger until the conversation is done
                                    continue;
                                }
                                else if (npc.CampaignInteractionType != CampaignMode.InteractionType.Examine)
                                {
                                    npc.CampaignInteractionType = CampaignMode.InteractionType.Examine;
                                    npc.RequireConsciousnessForCustomInteract = DisableIfTargetIncapacitated;
                                    npc.SetCustomInteract(
                                        (Character npc, Character interactor) => 
                                        { 
                                            //the first character in the CustomInteract callback is always the NPC and the 2nd the character who interacted with it
                                            //but the TriggerAction can configure the 1st and 2nd entity in either order,
                                            //let's make sure we pass the NPC and the interactor in the intended order
                                            if (e1 == npc && ParentEvent.GetTargets(Target2Tag).Contains(interactor)) 
                                            { 
                                                Trigger(npc, interactor); 
                                            } 
                                            else if (ParentEvent.GetTargets(Target1Tag).Contains(interactor) && e2 == npc)
                                            { 
                                                Trigger(interactor, npc); 
                                            } 
                                        },
#if CLIENT
                                        TextManager.GetWithVariable("CampaignInteraction.Examine", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.Use)));
#else
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
                                item.AssignCampaignInteractionType(CampaignMode.InteractionType.Examine, 
                                    GameMain.NetworkMember?.ConnectedClients.Where(c => c.Character != null && targets2.Contains(c.Character)));
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
                        if ((Type == TriggerType.Inside) == IsWithinRadius())
                        {
                            if (!CheckAllTargets)
                            {
                                Trigger(e1, e2);
                                return;
                            }
                            else
                            {
                                triggerers.Add((e1, e2));
                            }
                        }
                        else if (CheckAllTargets)
                        {
                            return;
                        }

                        bool IsWithinRadius() =>
                            ((e1 is MapEntity m1) && Submarine.RectContains(m1.WorldRect, pos2)) ||
                            ((e2 is MapEntity m2) && Submarine.RectContains(m2.WorldRect, pos1)) ||
                            Vector2.DistanceSquared(pos1, pos2) < Radius * Radius;
                    }
                }
            }

            foreach (var (e1, e2) in triggerers)
            {
                Trigger(e1, e2);
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
                    item.AssignCampaignInteractionType(CampaignMode.InteractionType.None);
                }
            }
        }

        private bool CheckDistanceToHull(Entity e, out Hull hull)
        {
            hull = null;
            if (Radius <= 0)
            {
                if (e is Character character && character.CurrentHull != null && character.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = character.CurrentHull;
                    return Type == TriggerType.Inside;
                }
                else if (e is Item item && item.CurrentHull != null && item.CurrentHull.OutpostModuleTags.Contains(TargetModuleType))
                {
                    hull = item.CurrentHull;
                    return Type == TriggerType.Inside;
                }
                return Type == TriggerType.Outside;
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
                        return Type == TriggerType.Inside;
                    }
                }
                return Type == TriggerType.Outside;
            }
        }

        private static bool IsInCombat(Entity entity)
        {
            if (entity is not Character character) { return false; }
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
                else if (c.AIController is EnemyAIController { State: AIState.Aggressive or AIState.Attack } enemyAI)
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

            Character player = null;
            Entity target = null;
            if (entity1 is Character { IsPlayer: true })
            {
                player = entity1 as Character;
                target = entity2;
            }
            else if (entity2 is Character { IsPlayer: true })
            {
                player = entity2 as Character;
                target = entity1;
            }
            if (player != null && SelectOnTrigger)
            {
                if (target is Character targetCharacter)
                {
                    player.SelectCharacter(targetCharacter);
                }
                else if (target is Item targetItem)
                {
                    if (targetItem.IsSecondaryItem)
                    {
                        player.SelectedSecondaryItem = targetItem;
                    }
                    else
                    {
                        player.SelectedItem = targetItem;
                    }
                }
            }

            isRunning = false;
            isFinished = true;
        }

        public override string ToDebugString()
        {
            if (TargetModuleType.IsEmpty)
            {
                string targetStr = "none";
                if (npcsOrItems.Any())
                {
                    targetStr = string.Join(", ", 
                        npcsOrItems.Select(npcOrItem => 
                            npcOrItem.TryGet(out Character character) ? character.Name : (npcOrItem.TryGet(out Item item) ? item.Name : "none")));
                }

                return
                    $"{ToolBox.GetDebugSymbol(isFinished, isRunning)} {nameof(TriggerAction)} -> (" +
                    (WaitForInteraction ?
                        $"Selected non-player target: {targetStr.ColorizeObject()}, " :
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