using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ScriptedEvent : Event
    {
        public sealed record TargetPredicate(
            TargetPredicate.EntityType Type,
            Predicate<Entity> Predicate)
        {
            public enum EntityType
            {
                Character,
                Hull,
                Item,
                Structure,
                Submarine
            }
        }

        private readonly Dictionary<Identifier, List<TargetPredicate>> targetPredicates = new Dictionary<Identifier, List<TargetPredicate>>();

        private readonly Dictionary<Identifier, List<Entity>> cachedTargets = new Dictionary<Identifier, List<Entity>>();

        /// <summary>
        /// How many targets were there when they were tagged for the first time? Can be used by some EventActions to check how many entities
        /// there are still left (e.g. how much of the initial cargo still exists)
        /// </summary>
        private readonly Dictionary<Identifier, int> initialAmounts = new Dictionary<Identifier, int>();

        private bool newEntitySpawned;
        private int prevPlayerCount, prevBotCount;
        private Character prevControlled;

        public readonly OnRoundEndAction OnRoundEndAction;

        private readonly Identifier[] requiredDestinationTypes;
        public readonly bool RequireBeaconStation;

        public readonly Identifier RequiredDestinationFaction;

        public int CurrentActionIndex { get; private set; }
        public List<EventAction> Actions { get; } = new List<EventAction>();
        public Dictionary<Identifier, List<Entity>> Targets { get; } = new Dictionary<Identifier, List<Entity>>();

        protected virtual IEnumerable<Identifier> NonActionChildElementNames => Enumerable.Empty<Identifier>();

        public override string ToString()
        {
            return $"{nameof(ScriptedEvent)} ({prefab.Identifier})";
        }
        
        public ScriptedEvent(EventPrefab prefab, int seed) : base(prefab, seed)
        {
            foreach (var element in prefab.ConfigElement.Elements())
            {
                Identifier elementId = element.Name.ToIdentifier();
                if (NonActionChildElementNames.Contains(elementId)) { continue; }
                if (elementId == nameof(Barotrauma.OnRoundEndAction))
                {
                    OnRoundEndAction = EventAction.Instantiate(this, element) as OnRoundEndAction;
                    continue;
                }
                if (elementId == "statuseffect")
                {
                    DebugConsole.ThrowError($"Error in event prefab \"{prefab.Identifier}\". Status effect configured as an action. Please configure status effects as child elements of a StatusEffectAction.", 
                        contentPackage: prefab.ContentPackage);
                    continue;
                }
                var action = EventAction.Instantiate(this, element);
                if (action != null) { Actions.Add(action); }
            }

            if (!Actions.Any())
            {
                DebugConsole.ThrowError($"Scripted event \"{prefab.Identifier}\" has no actions. The event will do nothing.", 
                    contentPackage: prefab.ContentPackage);
            }

            requiredDestinationTypes = prefab.ConfigElement.GetAttributeIdentifierArray("requireddestinationtypes", Array.Empty<Identifier>());
            RequireBeaconStation = prefab.ConfigElement.GetAttributeBool("requirebeaconstation", false);
            RequiredDestinationFaction = prefab.ConfigElement.GetAttributeIdentifier(nameof(RequiredDestinationFaction), Identifier.Empty);

            var allActionsWithIndent = GetAllActions();
            var allActions = allActionsWithIndent.Select(a => a.action);

            //attempt to check if the event has ConversationActions with options that don't close the prompt and don't lead to any follow-up conversation
            foreach (var action in allActions)
            {
                if (action is ConversationAction conversationAction && conversationAction.Options.Any())
                {
                    int thisActionIndex = allActionsWithIndent.FindIndex(a => a.action == action);
                    int thisIndentationLevel = allActionsWithIndent[thisActionIndex].indent;
                    bool isLast = true;

                    //go through all the actions after this one
                    foreach (var actionWithIndent in allActionsWithIndent.Skip(thisActionIndex + 1))
                    {
                        //if it's an action with the same indentation level, it means it's a ConversationAction coming after this one
                        if (actionWithIndent.action is ConversationAction && actionWithIndent.indent == thisIndentationLevel)
                        {
                            isLast = false;
                            break;
                        }
                        //if the indentation level went back down, we've already searched everything inside this ConversationAction
                        if (actionWithIndent.indent < thisIndentationLevel) { break; }
                    }
                    if (isLast)
                    {
                        foreach (var option in conversationAction.Options)
                        {
                            if (!conversationAction.GetEndingOptions().Contains(conversationAction.Options.IndexOf(option)) &&
                                option.Actions.None(a => 
                                    a is ConversationAction || HasConversationSubAction(a) ||
                                    /* if there's a goto action explicitly set to end the conversation, assume it's intentional*/
                                    a is GoTo { EndConversation: false }))
                            {
                                DebugConsole.AddWarning($"Potential error in event \"{prefab.Identifier}\": {nameof(ConversationAction)} ({conversationAction.Text}) has an option ({option.Text}) that doesn't end the conversation, but could not find any follow-ups to the conversation.");
                            }
                        }
                    }
                }

                static bool HasConversationSubAction(EventAction action)
                {
                    foreach (var subAction in action.GetSubActions())
                    {
                        if (subAction is ConversationAction) { return true; }
                        if (HasConversationSubAction(subAction)) { return true; }
                    }
                    return false;
                }
            }

            foreach (var label in allActions.OfType<Label>())
            {
                if (allActions.None(a => a is GoTo gotoAction && label.Name == gotoAction.Name))
                {
                    //this can be safe, because a label with no gotos leading to it does nothing (but it's still a sign that something's misconfigured)
                    DebugConsole.AddWarning($"Error in event \"{prefab.Identifier}\". Could not find a GoTo matching the Label \"{label.Name}\".",
                        contentPackage: prefab.ContentPackage);
                }
            }

            foreach (var gotoAction in allActions.OfType<GoTo>())
            {
                int labelCount = allActions.Count(a => a is Label label && label.Name == gotoAction.Name);
                if (labelCount == 0)
                {                    
                    DebugConsole.ThrowError($"Error in event \"{prefab.Identifier}\". Could not find a label matching the GoTo \"{gotoAction.Name}\".", 
                        contentPackage: prefab.ContentPackage);
                }
                else if (labelCount > 1)
                {
                    DebugConsole.ThrowError($"Error in event \"{prefab.Identifier}\". Multiple labels with the name \"{gotoAction.Name}\".",
                        contentPackage: prefab.ContentPackage);
                }
            }

            GameAnalyticsManager.AddDesignEvent($"ScriptedEvent:{prefab.Identifier}:Start");
        }

        public override string GetDebugInfo()
        {
            EventAction currentAction = !IsFinished ? Actions[CurrentActionIndex] : null;

            string text = $"Finished: {IsFinished.ColorizeObject()}\n" +
                          $"Action index: {CurrentActionIndex.ColorizeObject()}\n" +
                          $"Current action: {currentAction?.ToDebugString() ?? ToolBox.ColorizeObject(null)}\n";

            text += "All actions:\n";
            text += GetAllActions().Aggregate(string.Empty, (current, action) => current + $"{new string(' ', action.indent * 6)}{action.action.ToDebugString()}\n");

            text += "Targets:\n";
            foreach (var (key, value) in Targets)
            {
                text += $"    {key.ColorizeObject()}: {value.Aggregate(string.Empty, (current, entity) => current + $"{entity.ColorizeObject()} ")}\n";
            }
            return text;
        }

        public virtual string GetTextForReplacementElement(string tag)
        {
            if (tag.StartsWith("eventtag:"))
            {
                string targetTag = tag["eventtag:".Length..];
                Entity target = GetTargets(targetTag.ToIdentifier()).FirstOrDefault();
                if (target != null)
                {
                    if (target is Item item) { return item.Name; }
                    if (target is Character character) { return character.Name; }
                    if (target is Hull hull) { return hull.DisplayName.Value; }
                    if (target is Submarine sub) { return sub.Info.DisplayName.Value; }
                    DebugConsole.AddWarning($"Failed to get the name of the event target {target} as a replacement for the tag {tag} in an event text.",
                        prefab.ContentPackage);
                    return target.ToString();
                }
                else
                {
                    return $"[target \"{targetTag}\" not found]";
                }
            }
            return string.Empty;
        }

        public virtual LocalizedString ReplaceVariablesInEventText(LocalizedString str)
        {
            return str;
        }

        /// <summary>
        /// Finds all actions in the ScriptedEvent using a depth-first search (recursively going through the subactions as well). 
        /// Returns a list of tuples where the first value is the indentation level (or "how deep in the hierarchy") the action is.
        /// </summary>
        public List<(int indent, EventAction action)> GetAllActions()
        {
            var list = new List<(int indent, EventAction action)>();
            foreach (EventAction eventAction in Actions)
            {
                list.AddRange(FindActionsRecursive(eventAction));
            }
            return list;

            static List<(int indent, EventAction action)> FindActionsRecursive(EventAction eventAction, int indent = 1)
            {
                var eventActions = new List<(int indent, EventAction action)> { (indent, eventAction) };
                indent++;
                foreach (var action in eventAction.GetSubActions())
                {
                    eventActions.AddRange(FindActionsRecursive(action, indent));
                }
                return eventActions;
            }
        }

        public void AddTarget(Identifier tag, Entity target)
        {
            if (target == null)
            {
                throw new ArgumentException($"Target was null (tag: {tag})");
            }
            if (target.Removed)
            {
                throw new ArgumentException($"Target has been removed (tag: {tag})");
            }
            if (Targets.ContainsKey(tag))
            {
                if (!Targets[tag].Contains(target))
                {
                    Targets[tag].Add(target);
                }
            }
            else
            {
                Targets.Add(tag, new List<Entity>() { target });
            }
            if (cachedTargets.ContainsKey(tag))
            {
                if (!cachedTargets[tag].Contains(target))
                {
                    cachedTargets[tag].Add(target);
                }
            }
            else
            {
                cachedTargets.Add(tag, Targets[tag].ToList());
            }
            if (!initialAmounts.ContainsKey(tag))
            {
                initialAmounts.Add(tag, cachedTargets[tag].Count);
            }
        }

        public void AddTargetPredicate(Identifier tag, TargetPredicate.EntityType entityType, Predicate<Entity> predicate)
        {
            if (!targetPredicates.ContainsKey(tag))
            {
                targetPredicates.Add(tag, new List<TargetPredicate>());
            }
            targetPredicates[tag].Add(new TargetPredicate(entityType, predicate));
            // force re-search for this tag
            if (cachedTargets.ContainsKey(tag))
            {
                cachedTargets.Remove(tag);
            }
        }

        public int GetInitialTargetCount(Identifier tag)
        {
            if (initialAmounts.TryGetValue(tag, out int count))
            {
                return count;
            }
            return 0;
        }

        public IEnumerable<Entity> GetTargets(Identifier tag)
        {
            if (cachedTargets.ContainsKey(tag))
            {
                if (cachedTargets[tag].Any(t => t.Removed))
                {
                    cachedTargets.Clear();
                }
                else
                {
                    return cachedTargets[tag];
                }
            }

            List<Entity> targetsToReturn = new List<Entity>();
            if (Targets.ContainsKey(tag)) 
            { 
                foreach (Entity e in Targets[tag])
                {
                    if (e.Removed) { continue; }
                    targetsToReturn.Add(e);
                }
            }
            if (targetPredicates.ContainsKey(tag))
            {
                foreach (var targetPredicate in targetPredicates[tag])
                {
                    IEnumerable<Entity> entityList = targetPredicate.Type switch
                    {
                        TargetPredicate.EntityType.Character => Character.CharacterList,
                        TargetPredicate.EntityType.Item => Item.ItemList,
                        TargetPredicate.EntityType.Structure => MapEntity.MapEntityList.Where(m => m is Structure),
                        TargetPredicate.EntityType.Hull => Hull.HullList,
                        TargetPredicate.EntityType.Submarine => Submarine.Loaded,
                        _ => Entity.GetEntities(),
                    };
                    foreach (Entity entity in entityList)
                    {
                        if (targetsToReturn.Contains(entity)) { continue; }
                        if (targetPredicate.Predicate(entity))
                        {
                            targetsToReturn.Add(entity);
                        }
                    }
                }
            }
            foreach (WayPoint wayPoint in WayPoint.WayPointList)
            {
                if (wayPoint.Tags.Contains(tag)) { targetsToReturn.Add(wayPoint); }
            }            
            if (Level.Loaded?.StartOutpost != null && 
                Level.Loaded.StartOutpost.Info.OutpostNPCs.TryGetValue(tag, out List<Character> outpostNPCs))
            {
                foreach (Character npc in outpostNPCs)
                {
                    if (npc.Removed || targetsToReturn.Contains(npc)) { continue; }
                    targetsToReturn.Add(npc);
                }
            }

            cachedTargets.Add(tag, targetsToReturn);
            if (!initialAmounts.ContainsKey(tag))
            {
                initialAmounts.Add(tag, targetsToReturn.Count);
            }
            return targetsToReturn;
        }

        public void InheritTags(Entity originalEntity, Entity newEntity)
        {
            foreach (var kvp in Targets)
            {
                if (kvp.Value.Contains(originalEntity))
                {
                    kvp.Value.Add(newEntity);
                }
            }
        }

        public void RemoveTag(Identifier tag)
        {
            if (tag.IsEmpty) { return; }
            if (Targets.ContainsKey(tag)) { Targets.Remove(tag); }
            if (cachedTargets.ContainsKey(tag)) { cachedTargets.Remove(tag); }
            if (targetPredicates.ContainsKey(tag)) { targetPredicates.Remove(tag);  }
        }

        public override void Update(float deltaTime)
        {
            int botCount = 0;
            int playerCount = 0;
            foreach (Character c in Character.CharacterList)
            {
                if (c.IsPlayer)
                {
                    playerCount++;
                }
                else if (c.IsBot)
                {
                    botCount++;
                }
            }

            if (botCount != prevBotCount || playerCount != prevPlayerCount || prevControlled != Character.Controlled || NeedsToRefreshCachedTargets())
            {
                cachedTargets.Clear();
                newEntitySpawned = false;
                prevBotCount = botCount;
                prevPlayerCount = playerCount;
                prevControlled = Character.Controlled;
            }
            
            if (!Actions.Any())
            {
                Finish();
                return;
            }

            var currentAction = Actions[CurrentActionIndex];
            if (!currentAction.CanBeFinished())
            {
                Finish();
                return;
            }

            string goTo = null;
            if (currentAction.IsFinished(ref goTo))
            {
                if (string.IsNullOrEmpty(goTo))
                {
                    CurrentActionIndex++;
                }
                else
                {
                    CurrentActionIndex = -1;
                    Actions.ForEach(a => a.Reset());
                    for (int i = 0; i < Actions.Count; i++)
                    {
                        if (Actions[i].SetGoToTarget(goTo))
                        {
                            CurrentActionIndex = i;
                            break;
                        }
                    }
                    if (CurrentActionIndex == -1)
                    {
                        DebugConsole.AddWarning($"Could not find the GoTo label \"{goTo}\" in the event \"{Prefab.Identifier}\". Ending the event.",
                            prefab.ContentPackage);
                    }
                }

                if (CurrentActionIndex >= Actions.Count || CurrentActionIndex < 0)
                {
                    Finish();
                }
            }
            else
            {
                currentAction.Update(deltaTime);
            }
        }

        private bool NeedsToRefreshCachedTargets()
        {
            if (newEntitySpawned) { return true; }
            foreach (var cachedTargetList in cachedTargets.Values)
            {
                foreach (var target in cachedTargetList)
                {
                    //one of the previously cached entities has been removed -> force refresh
                    if (target.Removed)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        public void EntitySpawned(Entity entity)
        {
            if (newEntitySpawned) { return; }
            if (entity is Character character &&
                Level.Loaded?.StartOutpost != null &&
                Level.Loaded.StartOutpost.Info.OutpostNPCs.Values.Any(npcList => npcList.Contains(character)))
            {
                newEntitySpawned = true;
                return;
            }
            //new entity matches one of the existing predicates -> force refresh
            foreach (var targetPredicateList in targetPredicates.Values)
            {
                foreach (var targetPredicate in targetPredicateList)
                {
                    if (targetPredicate.Predicate(entity))
                    {
                        newEntitySpawned = true;
                        return;
                    }
                }
            }
        }

        public override bool LevelMeetsRequirements()
        {
            var currLocation = GameMain.GameSession?.Campaign?.Map.CurrentLocation;
            if (currLocation?.Connections == null) { return true; }
            foreach (LocationConnection c in currLocation.Connections)
            {
                if (RequireBeaconStation && !c.LevelData.HasBeaconStation) { continue; }

                var otherLocation = c.OtherLocation(currLocation);
                if (!RequiredDestinationFaction.IsEmpty && otherLocation.Faction?.Prefab.Identifier != RequiredDestinationFaction) { continue; }

                if (requiredDestinationTypes.Contains(Tags.AnyOutpost) && otherLocation.HasOutpost() && otherLocation.Type.IsAnyOutpost) { return true; }
                if (requiredDestinationTypes.Any(t => otherLocation.Type.Identifier == t))
                {
                    return true;
                }
            }
            return RequiredDestinationFaction.IsEmpty && requiredDestinationTypes.None();
        }


        public override void Finish()
        {
            base.Finish();
            GameAnalyticsManager.AddDesignEvent($"ScriptedEvent:{prefab.Identifier}:Finished:{CurrentActionIndex}");
        }
    }
}
