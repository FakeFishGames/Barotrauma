using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class ScriptedEvent : Event
    {
        private readonly Dictionary<string, List<Predicate<Entity>>> targetPredicates = new Dictionary<string, List<Predicate<Entity>>>();

        private readonly Dictionary<string, List<Entity>> cachedTargets = new Dictionary<string, List<Entity>>();
        private int prevEntityCount;
        private int prevPlayerCount, prevBotCount;

        public int CurrentActionIndex { get; private set; }
        public List<EventAction> Actions { get; } = new List<EventAction>();
        public Dictionary<string, List<Entity>> Targets { get; } = new Dictionary<string, List<Entity>>();

        public override string ToString()
        {
            return "ScriptedEvent (" + prefab.EventType.ToString() +")";
        }
        
        public ScriptedEvent(EventPrefab prefab) : base(prefab)
        {
            foreach (XElement element in prefab.ConfigElement.Elements())
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
        }

        public void AddTarget(string tag, Entity target)
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

        public void AddTargetPredicate(string tag, Predicate<Entity> predicate)
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

        public IEnumerable<Entity> GetTargets(string tag)
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
                    if (targetPredicates[tag].Any(p => p(entity)))
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
                    if (npc.Removed) { continue; }
                    targetsToReturn.Add(npc);
                }
            }

            cachedTargets.Add(tag, targetsToReturn);            
            return targetsToReturn;
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
            if (Entity.EntityCount != prevEntityCount || botCount != prevBotCount || playerCount != prevPlayerCount)
            {
                cachedTargets.Clear();
                prevEntityCount = Entity.EntityCount;
                prevBotCount = botCount;
                prevPlayerCount = playerCount;
            }
            
            if (!Actions.Any())
            {
                Finished();
                return;
            }

            var currentAction = Actions[CurrentActionIndex];
            if (!currentAction.CanBeFinished())
            {
                Finished();
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
                    Finished();
                }
            }
            else
            {
                currentAction.Update(deltaTime);
            }
        }
    }
}
