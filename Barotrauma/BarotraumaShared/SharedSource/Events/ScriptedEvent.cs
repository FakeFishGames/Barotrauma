﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ScriptedEvent : Event
    {
        private readonly Dictionary<Identifier, List<Predicate<Entity>>> targetPredicates = new Dictionary<Identifier, List<Predicate<Entity>>>();

        private readonly Dictionary<Identifier, List<Entity>> cachedTargets = new Dictionary<Identifier, List<Entity>>();
        private int prevEntityCount;
        private int prevPlayerCount, prevBotCount;
        private Character prevControlled;

        private readonly string[] requiredDestinationTypes;
        public readonly bool RequireBeaconStation;

        public int CurrentActionIndex { get; private set; }
        public List<EventAction> Actions { get; } = new List<EventAction>();
        public Dictionary<Identifier, List<Entity>> Targets { get; } = new Dictionary<Identifier, List<Entity>>();

        public override string ToString()
        {
            return $"ScriptedEvent ({prefab.Identifier})";
        }
        
        public ScriptedEvent(EventPrefab prefab) : base(prefab)
        {
            foreach (var element in prefab.ConfigElement.Elements())
            {
                if (element.Name.ToString().Equals("statuseffect", StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.ThrowError($"Error in event prefab \"{prefab.Identifier}\". Status effect configured as an action. Please configure status effects as child elements of a StatusEffectAction.");
                    continue;
                }
                var action = EventAction.Instantiate(this, element);
                if (action != null) { Actions.Add(action); }
            }

            if (!Actions.Any())
            {
                DebugConsole.ThrowError($"Scripted event \"{prefab.Identifier}\" has no actions. The event will do nothing.");
            }

            requiredDestinationTypes = prefab.ConfigElement.GetAttributeStringArray("requireddestinationtypes", null);
            RequireBeaconStation = prefab.ConfigElement.GetAttributeBool("requirebeaconstation", false);

            GameAnalyticsManager.AddDesignEvent($"ScriptedEvent:{prefab.Identifier}:Start");
        }

        public void AddTarget(Identifier tag, Entity target)
        {
            if (target == null)
            {
                throw new System.ArgumentException("Target was null");
            }
            if (target.Removed)
            {
                throw new System.ArgumentException("Target has been removed");
            }
            if (!Targets.ContainsKey(tag))
            {
                Targets.Add(tag, new List<Entity>());
            }
            Targets[tag].Add(target);
            if (cachedTargets.ContainsKey(tag))
            {
                cachedTargets[tag].Add(target);
            }
            else
            {
                cachedTargets.Add(tag, new List<Entity> { target });
            }
        }

        public void AddTargetPredicate(Identifier tag, Predicate<Entity> predicate)
        {
            if (!targetPredicates.ContainsKey(tag))
            {
                targetPredicates.Add(tag, new List<Predicate<Entity>>());
            }
            targetPredicates[tag].Add(predicate);
            // force re-search for this tag
            if (cachedTargets.ContainsKey(tag))
            {
                cachedTargets.Remove(tag);
            }
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
                foreach (Entity entity in Entity.GetEntities())
                {
                    if (targetPredicates[tag].Any(p => p(entity)) && !targetsToReturn.Contains(entity))
                    {
                        targetsToReturn.Add(entity);
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
            return targetsToReturn;
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
            if (Entity.EntityCount != prevEntityCount || botCount != prevBotCount || playerCount != prevPlayerCount || prevControlled != Character.Controlled)
            {
                cachedTargets.Clear();
                prevEntityCount = Entity.EntityCount;
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

        public override bool LevelMeetsRequirements()
        {
            if (requiredDestinationTypes == null) { return true; }
            var currLocation = GameMain.GameSession?.Campaign?.Map.CurrentLocation;
            if (currLocation?.Connections == null) { return true; }
            foreach (LocationConnection c in currLocation.Connections)
            {
                if (RequireBeaconStation && !c.LevelData.HasBeaconStation) { continue; }
                if (requiredDestinationTypes.Any(t => c.OtherLocation(currLocation).Type.Identifier == t))
                {
                    return true;
                }
            }
            return false;
        }

        public override void Finish()
        {
            base.Finish();
            GameAnalyticsManager.AddDesignEvent($"ScriptedEvent:{prefab.Identifier}:Finished:{CurrentActionIndex}");
        }
    }
}
