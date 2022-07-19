using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Steam;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    static class SteamAchievementManager
    {
        private const float UpdateInterval = 1.0f;

        private static HashSet<Identifier> unlockedAchievements = new HashSet<Identifier>();

        public static bool CheatsEnabled = false;

        private static float updateTimer;

        /// <summary>
        /// Keeps track of things that have happened during the round
        /// </summary>
        class RoundData
        {
            public readonly List<Reactor> Reactors = new List<Reactor>();

            public readonly HashSet<Character> EnteredCrushDepth = new HashSet<Character>();
            public readonly HashSet<Character> ReactorMeltdown = new HashSet<Character>();

            public bool SubWasDamaged;
        }

        private static RoundData roundData;

        // Used for the Extravehicular Activity ("crewaway") achievement
        private static PathFinder pathFinder;
        private static readonly Dictionary<Character, CachedDistance> cachedDistances = new Dictionary<Character, CachedDistance>();

        public static void OnStartRound()
        {
            roundData = new RoundData();
            foreach (Item item in Item.ItemList)
            {
                Reactor reactor = item.GetComponent<Reactor>();
                if (reactor != null) { roundData.Reactors.Add(reactor); }
            }
            pathFinder = new PathFinder(WayPoint.WayPointList, false);
            cachedDistances.Clear();
        }

        public static void Update(float deltaTime)
        {
            if (GameMain.GameSession == null) { return; }
#if CLIENT
            if (GameMain.Client != null) { return; }
#endif

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) { return; }
            updateTimer = UpdateInterval;
            
            if (Level.Loaded != null && roundData != null && Screen.Selected == GameMain.GameScreen)
            {
                if (GameMain.GameSession.EventManager.CurrentIntensity > 0.99f)
                {
                    UnlockAchievement("maxintensity".ToIdentifier(), true, c => c != null && !c.IsDead && !c.IsUnconscious);
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (c.IsDead) { continue; }
                    //achievement for descending below crush depth and coming back
                    if (Timing.TotalTime > GameMain.GameSession.RoundStartTime + 30.0f)
                    {
                        if (c.Submarine != null && c.Submarine.AtDamageDepth || Level.Loaded.GetRealWorldDepth(c.WorldPosition.Y) > Level.Loaded.RealWorldCrushDepth)
                        {
                            roundData.EnteredCrushDepth.Add(c);
                        }
                        else if (Level.Loaded.GetRealWorldDepth(c.WorldPosition.Y) < Level.Loaded.RealWorldCrushDepth - 500.0f)
                        {
                            //all characters that have entered crush depth and are still alive get an achievement
                            if (roundData.EnteredCrushDepth.Contains(c)) { UnlockAchievement(c, "survivecrushdepth".ToIdentifier()); }
                        }
                    }
                }

                foreach (Submarine sub in Submarine.Loaded)
                {
                    foreach (Reactor reactor in roundData.Reactors)
                    {
                        if (reactor.Item.Condition <= 0.0f && reactor.Item.Submarine == sub)
                        {
                            //characters that were inside the sub during a reactor meltdown 
                            //get an achievement if they're still alive at the end of the round
                            foreach (Character c in Character.CharacterList)
                            {
                                if (!c.IsDead && c.Submarine == sub) roundData.ReactorMeltdown.Add(c);
                            }
                        }
                    }

                    //convert submarine velocity to km/h
                    Vector2 submarineVel = Physics.DisplayToRealWorldRatio * ConvertUnits.ToDisplayUnits(sub.Velocity) * 3.6f;
                    //achievement for going > 100 km/h
                    if (Math.Abs(submarineVel.X) > 100.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subhighvelocity".ToIdentifier(), true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }

                    //achievement for descending ridiculously deep
                    float realWorldDepth = sub.RealWorldDepth;
                    if (realWorldDepth > 5000.0f && Timing.TotalTime > GameMain.GameSession.RoundStartTime + 30.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subdeep".ToIdentifier(), true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }
                }

                if (!roundData.SubWasDamaged)
                {
                    roundData.SubWasDamaged = SubWallsDamaged(Submarine.MainSub);
                }
            }

            if (GameMain.GameSession != null)
            {
#if CLIENT
                if (Character.Controlled != null && !(GameMain.GameSession.GameMode is TestGameMode)) 
                { 
                    CheckMidRoundAchievements(Character.Controlled); 
                }
#else
                foreach (Client client in GameMain.Server.ConnectedClients)
                {
                    if (client.Character != null)
                    {
                        CheckMidRoundAchievements(client.Character);
                    }
                }
#endif          
            }
        }

        private static void CheckMidRoundAchievements(Character c)
        {
            if (c == null || c.Removed) { return; }

            if (c.HasEquippedItem("clownmask".ToIdentifier()) &&
                c.HasEquippedItem("clowncostume".ToIdentifier()))
            {
                UnlockAchievement(c, "clowncostume".ToIdentifier());
            }

            if (Submarine.MainSub != null && c.Submarine == null && c.SpeciesName == CharacterPrefab.HumanSpeciesName)
            {
                float requiredDist = 500 / Physics.DisplayToRealWorldRatio;
                float distSquared = Vector2.DistanceSquared(c.WorldPosition, Submarine.MainSub.WorldPosition);
                if (cachedDistances.TryGetValue(c, out var cachedDistance))
                {
                    if (cachedDistance.ShouldUpdateDistance(c.WorldPosition, Submarine.MainSub.WorldPosition))
                    {
                        cachedDistances.Remove(c);
                        cachedDistance = CalculateNewCachedDistance(c);
                        if (cachedDistance != null)
                        {
                            cachedDistances.Add(c, cachedDistance);
                        }
                    }
                }
                else
                {
                    cachedDistance = CalculateNewCachedDistance(c);
                    if (cachedDistance != null)
                    {
                        cachedDistances.Add(c, cachedDistance);
                    }
                }
                if (cachedDistance != null)
                {
                    distSquared = Math.Max(distSquared, cachedDistance.Distance * cachedDistance.Distance);
                }
                if (distSquared > requiredDist * requiredDist)
                {
                    UnlockAchievement(c, "crewaway".ToIdentifier());
                }

                static CachedDistance CalculateNewCachedDistance(Character c)
                {
                    pathFinder ??= new PathFinder(WayPoint.WayPointList, false);
                    var path = pathFinder.FindPath(ConvertUnits.ToSimUnits(c.WorldPosition), ConvertUnits.ToSimUnits(Submarine.MainSub.WorldPosition));
                    if (path.Unreachable) { return null; }
                    return new CachedDistance(c.WorldPosition, Submarine.MainSub.WorldPosition, path.TotalLength, Timing.TotalTime + Rand.Range(1.0f, 5.0f));
                }
            }
        }

        private static bool SubWallsDamaged(Submarine sub)
        {
            foreach (Structure structure in Structure.WallList)
            {
                if (structure.Submarine != sub || structure.HasBody) { continue; }
                for (int i = 0; i < structure.SectionCount; i++)
                {
                    if (structure.SectionIsLeaking(i))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static void OnBiomeDiscovered(Biome biome)
        {
            UnlockAchievement($"discover{biome.Identifier.Value.Replace(" ", "")}".ToIdentifier());
        }

        public static void OnItemRepaired(Item item, Character fixer)
        {
#if CLIENT
            if (GameMain.Client != null) { return; }
#endif
            if (fixer == null) { return; }
            
            UnlockAchievement(fixer, "repairdevice".ToIdentifier());
            UnlockAchievement(fixer, $"repair{item.Prefab.Identifier}".ToIdentifier());
        }

        public static void OnAfflictionRemoved(Affliction affliction, Character character)
        {
            if (affliction.Prefab.AchievementOnRemoved.IsEmpty) { return; }

#if CLIENT
            if (GameMain.Client != null) { return; }
#endif
            UnlockAchievement(character, affliction.Prefab.AchievementOnRemoved);
        }

        public static void OnCharacterRevived(Character character, Character reviver)
        {
#if CLIENT
            if (GameMain.Client != null) { return; }
#endif
            if (reviver == null) { return; }
            UnlockAchievement(reviver, "healcrit".ToIdentifier());
        }

        public static void OnCharacterKilled(Character character, CauseOfDeath causeOfDeath)
        {
#if CLIENT
            if (GameMain.Client != null || GameMain.GameSession == null) { return; }

            if (character != Character.Controlled &&
                causeOfDeath.Killer != null &&
                causeOfDeath.Killer == Character.Controlled)
            {
                IncrementStat(causeOfDeath.Killer, (character.IsHuman ? "humanskilled" : "monsterskilled").ToIdentifier(), 1);
            }
#elif SERVER
            if (character != causeOfDeath.Killer && causeOfDeath.Killer != null)
            {
                IncrementStat(causeOfDeath.Killer, (character.IsHuman ? "humanskilled" : "monsterskilled").ToIdentifier(), 1);
            }
#endif

            UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName}".ToIdentifier());
            if (character.CurrentHull != null)
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName}indoors".ToIdentifier());
            }
            if (character.SpeciesName.EndsWith("boss"))
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("boss", "")}".ToIdentifier());
                if (character.CurrentHull != null)
                {
                    UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("boss", "")}indoors".ToIdentifier());
                }
            }
            if (character.SpeciesName.EndsWith("_m"))
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("_m", "")}".ToIdentifier());
                if (character.CurrentHull != null)
                {
                    UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("_m", "")}indoors".ToIdentifier());
                }
            }

            if (character.HasEquippedItem("clownmask".ToIdentifier()) && 
                character.HasEquippedItem("clowncostume".ToIdentifier()) &&
                causeOfDeath.Killer != character)
            {
                UnlockAchievement(causeOfDeath.Killer, "killclown".ToIdentifier());
            }

            if (character.CharacterHealth?.GetAffliction("morbusinepoisoning") != null)
            {
                UnlockAchievement(causeOfDeath.Killer, "killpoison".ToIdentifier());
            }

            if (causeOfDeath.DamageSource is Item item)
            {
                if (item.HasTag("tool"))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtool".ToIdentifier());
                }
                else
                {
                    if (item.Prefab.Identifier == "morbusine")
                    {
                        UnlockAchievement(causeOfDeath.Killer, "killpoison".ToIdentifier());
                    }
                    else if (item.Prefab.Identifier == "nuclearshell" ||
                             item.Prefab.Identifier == "nucleardepthcharge")
                    {
                            UnlockAchievement(causeOfDeath.Killer, "killnuke".ToIdentifier());
                    }
                }
            }

#if SERVER
            if (GameMain.Server?.TraitorManager != null)
            {
                if (GameMain.Server.TraitorManager.IsTraitor(character))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtraitor".ToIdentifier());
                }
            }
#endif
        }

        public static void OnTraitorWin(Character character)
        {
#if CLIENT
            if (GameMain.Client != null || GameMain.GameSession == null) { return; }
#endif
            UnlockAchievement(character, "traitorwin".ToIdentifier());
        }

        public static void OnRoundEnded(GameSession gameSession)
        {
            if (CheatsEnabled) { return; }

            //made it to the destination
            if (gameSession?.Submarine != null && Level.Loaded != null && gameSession.Submarine.AtEndExit)
            {
                float levelLengthMeters = Physics.DisplayToRealWorldRatio * Level.Loaded.Size.X;
                float levelLengthKilometers = levelLengthMeters / 1000.0f;
                //in multiplayer the client's/host's character must be inside the sub (or end outpost) and alive
                if (GameMain.NetworkMember != null)
                {
#if CLIENT
                    Character myCharacter = Character.Controlled;
                    if (myCharacter != null &&
                        !myCharacter.IsDead &&
                        (myCharacter.Submarine == gameSession.Submarine || (Level.Loaded?.EndOutpost != null && myCharacter.Submarine == Level.Loaded.EndOutpost)))
                    {
                        IncrementStat("kmstraveled".ToIdentifier(), levelLengthKilometers);
                    }
#endif
                }
                else
                {
                    //in sp making it to the end is enough
                    IncrementStat("kmstraveled".ToIdentifier(), levelLengthKilometers);
                }
            }

            //make sure changed stats (kill count, kms traveled) get stored
            SteamManager.StoreStats();

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            foreach (Mission mission in gameSession.Missions)
            {
                if (mission is CombatMission combatMission && GameMain.GameSession.WinningTeam.HasValue)
                {
                    //all characters that are alive and in the winning team get an achievement
                    var achvIdentifier =
                        $"{mission.Prefab.AchievementIdentifier}{(int) GameMain.GameSession.WinningTeam}"
                            .ToIdentifier();
                    UnlockAchievement(achvIdentifier, true,
                        c => c != null && !c.IsDead && !c.IsUnconscious && combatMission.IsInWinningTeam(c));
                }
                else if (mission.Completed)
                {
                    //all characters get an achievement
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        UnlockAchievement(mission.Prefab.AchievementIdentifier, true, c => c != null);
                    }
                    else
                    {
                        UnlockAchievement(mission.Prefab.AchievementIdentifier);
                    }
                }
            }
            
            //made it to the destination
            if (gameSession.Submarine.AtEndExit)
            {
                bool noDamageRun = !roundData.SubWasDamaged && !gameSession.Casualties.Any();

#if SERVER
                if (GameMain.Server != null)
                {
                    //in MP all characters that were inside the sub during reactor meltdown and still alive at the end of the round get an achievement
                    UnlockAchievement("survivereactormeltdown".ToIdentifier(), true, c => c != null && !c.IsDead && roundData.ReactorMeltdown.Contains(c));
                    if (noDamageRun)
                    {
                        UnlockAchievement("nodamagerun".ToIdentifier(), true, c => c != null && !c.IsDead);                    
                    }
                }
#endif
#if CLIENT
                if (noDamageRun) { UnlockAchievement("nodamagerun".ToIdentifier()); }
                if (roundData.ReactorMeltdown.Any()) //in SP getting to the destination after a meltdown is enough
                {
                    UnlockAchievement("survivereactormeltdown".ToIdentifier());
                }
#endif
                var charactersInSub = Character.CharacterList.FindAll(c => 
                    !c.IsDead && 
                    c.TeamID != CharacterTeamType.FriendlyNPC &&
                    !(c.AIController is EnemyAIController) &&
                    (c.Submarine == gameSession.Submarine || gameSession.Submarine.GetConnectedSubs().Contains(c.Submarine) || (Level.Loaded?.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost)));

                if (charactersInSub.Count == 1)
                {
                    //there must be some casualties to get the last mant standing achievement
                    if (gameSession.Casualties.Any())
                    {
                        UnlockAchievement(charactersInSub[0], "lastmanstanding".ToIdentifier());
                    }
#if CLIENT
                    else if (GameMain.GameSession.CrewManager.GetCharacters().Count() == 1)
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor".ToIdentifier());
                    }
#else
                    //lone sailor achievement if alone in the sub and there are no other characters with the same team ID
                    else if (!Character.CharacterList.Any(c => 
                        c != charactersInSub[0] && 
                        c.TeamID == charactersInSub[0].TeamID && 
                        !(c.AIController is EnemyAIController)))
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor".ToIdentifier());
                    }
#endif

                }
                foreach (Character character in charactersInSub)
                {
                    if (roundData.EnteredCrushDepth.Contains(character))
                    {
                        UnlockAchievement(character, "survivecrushdepth".ToIdentifier());
                    }
                    if (character.Info.Job == null) { continue; }
                    UnlockAchievement(character, $"{character.Info.Job.Prefab.Identifier}round".ToIdentifier());
                }
            }

            pathFinder = null;
        }

        private static void UnlockAchievement(Character recipient, Identifier identifier)
        {
            if (CheatsEnabled || recipient == null) { return; }
#if CLIENT
            if (recipient == Character.Controlled)
            {
                UnlockAchievement(identifier);
            }
#elif SERVER
            GameMain.Server?.GiveAchievement(recipient, identifier);
#endif
        }

        private static void IncrementStat(Character recipient, Identifier identifier, int amount)
        {
            if (CheatsEnabled || recipient == null) { return; }
#if CLIENT
            if (recipient == Character.Controlled)
            {
                SteamManager.IncrementStat(identifier, amount);
            }
#elif SERVER
            GameMain.Server?.IncrementStat(recipient, identifier, amount);
#endif
        }

        public static void IncrementStat(Identifier identifier, int amount)
        {
            if (CheatsEnabled) { return; }
            SteamManager.IncrementStat(identifier, amount);
        }

        public static void IncrementStat(Identifier identifier, float amount)
        {
            if (CheatsEnabled) { return; }
            SteamManager.IncrementStat(identifier, amount);
        }

        public static void UnlockAchievement(Identifier identifier, bool unlockClients = false, Func<Character, bool> conditions = null)
        {
            if (CheatsEnabled) { return; }

#if SERVER
            if (unlockClients && GameMain.Server != null)
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (conditions != null && !conditions(c.Character)) { continue; }
                    GameMain.Server.GiveAchievement(c, identifier);
                }
            }
#endif
            //already unlocked, no need to do anything
            if (unlockedAchievements.Contains(identifier)) { return; }
            unlockedAchievements.Add(identifier);

#if CLIENT
            if (conditions != null && !conditions(Character.Controlled)) { return; }
#endif

            SteamManager.UnlockAchievement(identifier);
        }
    }
}
