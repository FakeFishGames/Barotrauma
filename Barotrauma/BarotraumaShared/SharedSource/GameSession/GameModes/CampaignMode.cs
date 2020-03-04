using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public readonly CargoManager CargoManager;

        public bool CheatsEnabled;

        const int InitialMoney = 8700;
        public const int HullRepairCost = 500, ItemRepairCost = 500, ShuttleReplaceCost = 1000;

        protected bool watchmenSpawned;
        protected Character startWatchman, endWatchman;

        //key = dialog flag, double = Timing.TotalTime when the line was last said
        private Dictionary<string, double> dialogLastSpoken = new Dictionary<string, double>();

        public bool PurchasedHullRepairs, PurchasedLostShuttles, PurchasedItemRepairs;

        public bool InitialSuppliesSpawned;

        protected Map map;
        public Map Map
        {
            get { return map; }
        }

        public override Mission Mission
        {
            get
            {
                return Map.CurrentLocation?.SelectedMission;
            }
        }

        private int money;
        public int Money
        {
            get { return money; }
            set { money = Math.Max(value, 0); }
        }

        public CampaignMode(GameModePreset preset, object param)
            : base(preset, param)
        {
            Money = InitialMoney;
            CargoManager = new CargoManager(this);            
        }

        public void GenerateMap(string seed)
        {
            map = new Map(seed);
        }

        protected List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(s =>
                s != leavingSub &&
                !leavingSub.DockedTo.Contains(s) &&
                s != Level.Loaded.StartOutpost && s != Level.Loaded.EndOutpost &&
                (s.AtEndPosition != leavingSub.AtEndPosition || s.AtStartPosition != leavingSub.AtStartPosition));
        }

        public override void Start()
        {
            base.Start();
            dialogLastSpoken.Clear();
            watchmenSpawned = false;
            startWatchman = null;
            endWatchman = null;

            if (PurchasedHullRepairs)
            {
                foreach (Structure wall in Structure.WallList)
                {
                    if (wall.Submarine == null || wall.Submarine.IsOutpost) { continue; }
                    if (wall.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(wall.Submarine))
                    {
                        for (int i = 0; i < wall.SectionCount; i++)
                        {
                            wall.AddDamage(i, -wall.Prefab.Health);
                        }
                    }
                }
                PurchasedHullRepairs = false;
            }
            if (PurchasedItemRepairs)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.IsOutpost) { continue; }
                    if (item.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(item.Submarine))
                    {
                        if (item.GetComponent<Items.Components.Repairable>() != null)
                        {
                            item.Condition = item.Prefab.Health;
                        }
                    }
                }
                PurchasedItemRepairs = false;
            }
            PurchasedLostShuttles = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!IsRunning) { return; }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (!watchmenSpawned)
            {
                if (Level.Loaded.StartOutpost != null) { startWatchman = SpawnWatchman(Level.Loaded.StartOutpost); }
                if (Level.Loaded.EndOutpost != null) { endWatchman = SpawnWatchman(Level.Loaded.EndOutpost); }
                watchmenSpawned = true;
#if SERVER
                (this as MultiPlayerCampaign).LastUpdateID++;
#endif
            }
            else
            {
                foreach (Character character in Character.CharacterList)
                {
#if SERVER
                    if (string.IsNullOrEmpty(character.OwnerClientEndPoint)) { continue; }
#else
                    if (!CrewManager.GetCharacters().Contains(character)) { continue; }
#endif
                    if (character.Submarine == Level.Loaded.StartOutpost &&
                        Vector2.DistanceSquared(character.WorldPosition, startWatchman.WorldPosition) < 500.0f * 500.0f)
                    {
                        CreateDialog(new List<Character> { startWatchman }, "EnterStartOutpost", 5 * 60.0f);
                    }
                    else if (character.Submarine == Level.Loaded.EndOutpost &&
                        Vector2.DistanceSquared(character.WorldPosition, endWatchman.WorldPosition) < 500.0f * 500.0f)
                    {
                        CreateDialog(new List<Character> { endWatchman }, "EnterEndOutpost", 5 * 60.0f);
                    }
                }
            }
        }

        protected void CreateDialog(List<Character> speakers, string conversationTag, float minInterval)
        {
            if (dialogLastSpoken.TryGetValue(conversationTag, out double lastTime))
            {
                if (Timing.TotalTime - lastTime < minInterval) { return; }
            }

            CrewManager.AddConversation(
                NPCConversation.CreateRandom(speakers, new List<string>() { conversationTag }));
            dialogLastSpoken[conversationTag] = Timing.TotalTime;
        }

        private Character SpawnWatchman(Submarine outpost)
        {
            WayPoint watchmanSpawnpoint = WayPoint.WayPointList.Find(wp => wp.Submarine == outpost);
            if (watchmanSpawnpoint == null)
            {
                DebugConsole.ThrowError("Failed to spawn a watchman at the outpost. No spawnpoints found inside the outpost.");
                return null;
            }

            string seed = outpost == Level.Loaded.StartOutpost ? map.SelectedLocation.Name : map.CurrentLocation.Name;
            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            JobPrefab watchmanJob = JobPrefab.Get("watchman");
            var variant = Rand.Range(0, watchmanJob.Variants, Rand.RandSync.Server);
            CharacterInfo characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: watchmanJob, variant: variant);
            var spawnedCharacter = Character.Create(characterInfo, watchmanSpawnpoint.WorldPosition,
                Level.Loaded.Seed + (outpost == Level.Loaded.StartOutpost ? "start" : "end"));
            InitializeWatchman(spawnedCharacter);
            var objectiveManager = (spawnedCharacter.AIController as HumanAIController)?.ObjectiveManager;
            if (objectiveManager != null)
            {
                var moveOrder = new AIObjectiveGoTo(watchmanSpawnpoint, spawnedCharacter, objectiveManager, repeat: true, getDivingGearIfNeeded: false);
                moveOrder.Completed += () =>
                {
                    // Turn towards the center of the sub. Doesn't work in all possible cases, but this is the simplest solution for now.
                    spawnedCharacter.AnimController.TargetDir = spawnedCharacter.Submarine.WorldPosition.X > spawnedCharacter.WorldPosition.X ? Direction.Right : Direction.Left;
                };
                objectiveManager.SetOrder(moveOrder);
            }
            if (watchmanJob != null)
            {
                spawnedCharacter.GiveJobItems();
            }
            return spawnedCharacter;
        }

        protected void InitializeWatchman(Character character)
        {
            character.CharacterHealth.UseHealthWindow = false;
            character.CharacterHealth.Unkillable = true;
            character.CanInventoryBeAccessed = false;
            character.CanBeDragged = false;
            character.TeamID = Character.TeamType.FriendlyNPC;
            character.SetCustomInteract(
                WatchmanInteract,
#if CLIENT 
                hudText: TextManager.GetWithVariable("TalkHint", "[key]", GameMain.Config.KeyBindText(InputType.Select)));
#else
                hudText: TextManager.Get("TalkHint"));
#endif
        }

        protected abstract void WatchmanInteract(Character watchman, Character interactor);
        
        public abstract void Save(XElement element);
        
        public void LogState()
        {
            DebugConsole.NewMessage("********* CAMPAIGN STATUS *********", Color.White);
            DebugConsole.NewMessage("   Money: " + Money, Color.White);
            DebugConsole.NewMessage("   Current location: " + map.CurrentLocation.Name, Color.White);

            DebugConsole.NewMessage("   Available destinations: ", Color.White);
            for (int i = 0; i < map.CurrentLocation.Connections.Count; i++)
            {
                Location destination = map.CurrentLocation.Connections[i].OtherLocation(map.CurrentLocation);
                if (destination == map.SelectedLocation)
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name + " [SELECTED]", Color.White);
                }
                else
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name, Color.White);
                }
            }
            
            if (map.CurrentLocation?.SelectedMission != null)
            {
                DebugConsole.NewMessage("   Selected mission: " + map.CurrentLocation.SelectedMission.Name, Color.White);
                DebugConsole.NewMessage("\n" + map.CurrentLocation.SelectedMission.Description, Color.White);
            }
        }

        public override void Remove()
        {
            base.Remove();
            map?.Remove();
            map = null;
        }
    }
}
