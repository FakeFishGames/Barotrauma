using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class OutpostDestroyMission : AbandonedOutpostMission
    {
        private readonly string itemTag;
        private readonly XElement itemConfig;
        private readonly List<Item> items = new List<Item>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                if (State > 0)
                {
                    return Enumerable.Empty<Vector2>();
                }
                else
                {
                    return Targets.Select(t => t.WorldPosition);
                }
            }
        }

        private IEnumerable<Entity> Targets
        {
            get
            {
                if (State > 0)
                {
                    return Enumerable.Empty<Entity>();
                }
                else
                {
                    if (items.Any())
                    {
                        return items.Where(it => !it.Removed && it.Condition > 0.0f).Cast<Entity>().Concat(requireKill.Where(c => !c.Removed && !c.IsDead)).Concat(requireRescue);
                    }
                    else
                    {
                        return requireKill.Concat(requireRescue);
                    }
                }
            }
        }

        public OutpostDestroyMission(MissionPrefab prefab, Location[] locations) : 
            base(prefab, locations)
        {
            itemConfig = prefab.ConfigElement.Element("Items");
            itemTag = prefab.ConfigElement.GetAttributeString("targetitem", "");
        }

        protected override void StartMissionSpecific(Level level)
        {
            items.Clear();
#if SERVER
            spawnedItems.Clear();
#endif
            if (!string.IsNullOrEmpty(itemTag))
            {
                var itemsToDestroy = Item.ItemList.FindAll(it => it.Submarine?.Info.Type != SubmarineType.Player && it.HasTag(itemTag));
                if (!itemsToDestroy.Any())
                {
                    DebugConsole.ThrowError($"Error in mission \"{Prefab.Identifier}\". Could not find an item with the tag \"{itemTag}\".");
                }
                else
                {
                    items.AddRange(itemsToDestroy);
                }
            }
            if (itemConfig != null && !IsClient)
            {
                foreach (XElement element in itemConfig.Elements())
                {
                    string itemIdentifier = element.GetAttributeString("identifier", "");
                    if (!(MapEntityPrefab.Find(null, itemIdentifier) is ItemPrefab itemPrefab))
                    {
                        DebugConsole.ThrowError("Couldn't spawn item for outpost destroy mission: item prefab \"" + itemIdentifier + "\" not found");
                        continue;
                    }

                    string[] moduleFlags = element.GetAttributeStringArray("moduleflags", null);
                    string[] spawnPointTags = element.GetAttributeStringArray("spawnpointtags", null); 
                    ISpatialEntity spawnPoint = SpawnAction.GetSpawnPos(
                         SpawnAction.SpawnLocationType.Outpost, SpawnType.Human | SpawnType.Enemy,
                         moduleFlags, spawnPointTags, element.GetAttributeBool("asfaraspossible", false));
                    if (spawnPoint == null)
                    {
                        var submarine = Submarine.Loaded.Find(s => s.Info.Type == SubmarineType.Outpost) ?? Submarine.MainSub;
                        spawnPoint = submarine.GetHulls(alsoFromConnectedSubs: false).GetRandom();
                    }
                    Vector2 spawnPos = spawnPoint.WorldPosition;
                    if (spawnPoint is WayPoint wp && wp.CurrentHull != null)
                    {
                        spawnPos = new Vector2(
                            MathHelper.Clamp(wp.WorldPosition.X + Rand.Range(-200, 200), wp.CurrentHull.WorldRect.X, wp.CurrentHull.WorldRect.Right),
                            wp.CurrentHull.WorldRect.Y - wp.CurrentHull.Rect.Height + 10.0f);
                    }
                    var item = new Item(itemPrefab, spawnPos, null);
                    items.Add(item);
#if SERVER
                    spawnedItems.Add(item);
#endif
                }
            }

            base.StartMissionSpecific(level);
        }

        public override void Update(float deltaTime)
        {
            if (requireRescue.Any(r => r.Removed || r.IsDead))
            {
#if SERVER
                if (!(GameMain.GameSession.GameMode is CampaignMode) && GameMain.Server != null)
                {
                    GameMain.Server.EndGame();                        
                }
#endif
                return;
            }

            switch (state)
            {
                case 0:
                    if (items.Any())
                    {
                        if (items.All(it => it.Removed || it.Condition <= 0.0f) &&
                            requireKill.All(c => c.Removed || c.IsDead) && 
                            requireRescue.All(c => c.Submarine?.Info.Type == SubmarineType.Player))
                        {
                            State = 1;
                        }
                    }
                    else
                    {
                        if (requireKill.All(c => c.Removed || c.IsDead) &&
                            requireRescue.All(c => c.Submarine?.Info.Type == SubmarineType.Player))
                        {
                            State = 1;
                        }
                    }
                    break;
#if SERVER
                case 1:
                    if (!(GameMain.GameSession.GameMode is CampaignMode) && GameMain.Server != null)
                    {
                        if (!Submarine.MainSub.AtStartExit || (wasDocked && !Submarine.MainSub.DockedTo.Contains(Level.Loaded.StartOutpost)))
                        {
                            GameMain.Server.EndGame();
                            State = 2;
                        }
                    }
                    break;
#endif
            }
        }
    }
}