using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class CampaignMode : GameMode
    {
        public readonly CargoManager CargoManager;

        public bool CheatsEnabled;

        const int InitialMoney = 10000;

        private bool watchmenSpawned;
        private Character startWatchman, endWatchman;

        //key = dialog flag, double = Timing.TotalTime when the line was last said
        private Dictionary<string, double> dialogLastSpoken = new Dictionary<string, double>();

        protected Map map;
        public Map Map
        {
            get { return map; }
        }

        public override Mission Mission
        {
            get
            {
                return Map.SelectedConnection.Mission;
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
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (GameMain.Client != null || !IsRunning) { return; }

            if (!watchmenSpawned)
            {
                if (Level.Loaded.StartOutpost != null) { startWatchman = SpawnWatchman(Level.Loaded.StartOutpost); }
                if (Level.Loaded.EndOutpost != null) { endWatchman = SpawnWatchman(Level.Loaded.EndOutpost); }
                watchmenSpawned = true;
            }
            else
            {
                foreach (Character character in Character.CharacterList)
                {
#if SERVER
                    if (string.IsNullOrEmpty(character.OwnerClientIP)) { continue; }
#else
                    if (!CrewManager.GetCharacters().Contains(character)) { continue; }
#endif
                    if (character.Submarine == Level.Loaded.StartOutpost && character.CurrentHull == startWatchman.CurrentHull)
                    {
                        CreateDialog(new List<Character> { startWatchman }, "EnterStartOutpost", 5 * 60.0f);
                    }
                    else if (character.Submarine == Level.Loaded.EndOutpost && character.CurrentHull == endWatchman.CurrentHull)
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

            JobPrefab watchmanJob = JobPrefab.List.Find(jp => jp.Identifier == "watchman");
            CharacterInfo characterInfo = new CharacterInfo(Character.HumanConfigFile, jobPrefab: watchmanJob);
            var spawnedCharacter = Character.Create(characterInfo, watchmanSpawnpoint.WorldPosition,
                Level.Loaded.Seed + (outpost == Level.Loaded.StartOutpost ? "start" : "end"));
            spawnedCharacter.CharacterHealth.Unkillable = true;
            spawnedCharacter.CharacterHealth.UseHealthWindow = false;
            spawnedCharacter.SetCustomInteract(
                WatchmanInteract,
                hudText: TextManager.Get("TalkHint").Replace("[key]", GameMain.Config.KeyBind(InputType.Select).ToString()));
            (spawnedCharacter.AIController as HumanAIController)?.ObjectiveManager.SetOrder(
                new AIObjectiveGoTo(watchmanSpawnpoint, spawnedCharacter, repeat: true, getDivingGearIfNeeded: false));
            if (watchmanJob != null)
            {
                spawnedCharacter.GiveJobItems();
            }
            return spawnedCharacter;
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
            
            if (map.SelectedConnection?.Mission != null)
            {
                DebugConsole.NewMessage("   Selected mission: " + map.SelectedConnection.Mission.Name, Color.White);
                DebugConsole.NewMessage("\n" + map.SelectedConnection.Mission.Description, Color.White);
            }
        }
    }
}
