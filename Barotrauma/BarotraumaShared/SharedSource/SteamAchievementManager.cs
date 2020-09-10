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

        private static HashSet<string> unlockedAchievements = new HashSet<string>();

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

            public readonly HashSet<Character> Casualties = new HashSet<Character>();

            public bool SubWasDamaged;
        }

        private static RoundData roundData;

        public static void OnStartRound()
        {
            roundData = new RoundData();
            foreach (Item item in Item.ItemList)
            {
                Reactor reactor = item.GetComponent<Reactor>();
                if (reactor != null) roundData.Reactors.Add(reactor);
            }
        }

        public static void Update(float deltaTime)
        {
            if (GameMain.GameSession == null) return;
#if CLIENT
            if (GameMain.Client != null) return;
#endif

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) return;
            updateTimer = UpdateInterval;
            
            if (Level.Loaded != null)
            {
                if (GameMain.GameSession.EventManager.CurrentIntensity > 0.99f)
                {
                    UnlockAchievement("maxintensity", true, c => c != null && !c.IsDead && !c.IsUnconscious);
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (c.IsDead) { continue; }
                    //achievement for descending below crush depth and coming back
                    if (Timing.TotalTime > GameMain.GameSession.RoundStartTime + 30.0f)
                    {
                        if (c.WorldPosition.Y < SubmarineBody.DamageDepth || (c.Submarine != null && c.Submarine.WorldPosition.Y < SubmarineBody.DamageDepth))
                        {
                            roundData.EnteredCrushDepth.Add(c);
                        }
                        else if (c.WorldPosition.Y > SubmarineBody.DamageDepth * 0.5f)
                        {
                            //all characters that have entered crush depth and are still alive get an achievement
                            if (roundData.EnteredCrushDepth.Contains(c)) UnlockAchievement(c, "survivecrushdepth");
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
                        UnlockAchievement("subhighvelocity", true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }

                    //achievement for descending ridiculously deep
                    float realWorldDepth = Math.Abs(sub.Position.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                    if (realWorldDepth > 5000.0f && Timing.TotalTime > GameMain.GameSession.RoundStartTime + 30.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subdeep", true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
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

            if (c.HasEquippedItem("clownmask") &&
                c.HasEquippedItem("clowncostume"))
            {
                UnlockAchievement(c, "clowncostume");
            }

            if (Submarine.MainSub != null && c.Submarine == null && c.SpeciesName.Equals(CharacterPrefab.HumanSpeciesName, StringComparison.OrdinalIgnoreCase))
            {
                float dist = 500 / Physics.DisplayToRealWorldRatio;
                if (Vector2.DistanceSquared(c.WorldPosition, Submarine.MainSub.WorldPosition) >
                    dist * dist)
                {
                    UnlockAchievement(c, "crewaway");
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
            UnlockAchievement("discover" + biome.Identifier.ToLowerInvariant().Replace(" ", ""));
        }

        public static void OnItemRepaired(Item item, Character fixer)
        {
#if CLIENT
            if (GameMain.Client != null) return;
#endif
            if (fixer == null) return;
            
            UnlockAchievement(fixer, "repairdevice");
            UnlockAchievement(fixer, "repair" + item.Prefab.Identifier);
        }

        public static void OnAfflictionRemoved(Affliction affliction, Character character)
        {
            if (string.IsNullOrEmpty(affliction.Prefab.AchievementOnRemoved)) return;

#if CLIENT
            if (GameMain.Client != null) return;
#endif
            UnlockAchievement(character, affliction.Prefab.AchievementOnRemoved);
        }

        public static void OnCharacterRevived(Character character, Character reviver)
        {
#if CLIENT
            if (GameMain.Client != null) return;
#endif
            if (reviver == null) return;
            UnlockAchievement(reviver, "healcrit");
        }

        public static void OnCharacterKilled(Character character, CauseOfDeath causeOfDeath)
        {
#if CLIENT
            if (GameMain.Client != null || GameMain.GameSession == null) return;
#endif

            if (character != Character.Controlled &&
                causeOfDeath.Killer != null &&
                causeOfDeath.Killer == Character.Controlled)
            {
                SteamManager.IncrementStat(
                    character.IsHuman ? "humanskilled" : "monsterskilled",
                    1);
            }

            roundData?.Casualties.Add(character);

            UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName);
            if (character.CurrentHull != null)
            {
                UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName + "indoors");
            }
            if (character.SpeciesName.EndsWith("boss"))
            {
                UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName.Replace("boss", ""));
                if (character.CurrentHull != null)
                {
                    UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName.Replace("boss", "") + "indoors");
                }
            }

            if (character.HasEquippedItem("clownmask") && 
                character.HasEquippedItem("clowncostume") &&
                causeOfDeath.Killer != character)
            {
                UnlockAchievement(causeOfDeath.Killer, "killclown");
            }

            if (causeOfDeath.DamageSource is Item item)
            {
                if (item.ItemTags.HasTag("tool"))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtool");
                }
                else
                {
                    switch (item.Prefab.Identifier)
                    {
                        case "morbusine":
                            UnlockAchievement(causeOfDeath.Killer, "killpoison");
                            break;
                        case "nuclearshell":
                        case "nucleardepthcharge":
                            UnlockAchievement(causeOfDeath.Killer, "killnuke");
                            break;
                    }
                }
            }

#if SERVER
            if (GameMain.Server?.TraitorManager != null)
            {
                if (GameMain.Server.TraitorManager.IsTraitor(character))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtraitor");
                }
            }
#endif
        }

        public static void OnTraitorWin(Character character)
        {
#if CLIENT
            if (GameMain.Client != null || GameMain.GameSession == null) return;
#endif
            UnlockAchievement(character, "traitorwin");
        }

        public static void OnRoundEnded(GameSession gameSession)
        {
            //made it to the destination
            if (gameSession?.Submarine != null && Level.Loaded != null && gameSession.Submarine.AtEndPosition)
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
                        SteamManager.IncrementStat("kmstraveled", levelLengthKilometers);
                    }
#endif
                }
                else
                {
                    //in sp making it to the end is enough
                    SteamManager.IncrementStat("kmstraveled", levelLengthKilometers);
                }
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (gameSession.Mission != null)
            {
                if (gameSession.Mission is CombatMission combatMission && GameMain.GameSession.WinningTeam.HasValue)
                {
                    //all characters that are alive and in the winning team get an achievement
                    UnlockAchievement(gameSession.Mission.Prefab.AchievementIdentifier + (int)GameMain.GameSession.WinningTeam, true, 
                        c => c != null && !c.IsDead && !c.IsUnconscious && combatMission.IsInWinningTeam(c));
                }
                else if (gameSession.Mission.Completed)
                {
                    //all characters get an achievement
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        UnlockAchievement(gameSession.Mission.Prefab.AchievementIdentifier, true, c => c != null);
                    }
                    else
                    {
                        UnlockAchievement(gameSession.Mission.Prefab.AchievementIdentifier);
                    }
                }
            }
            
            //made it to the destination
            if (gameSession.Submarine.AtEndPosition)
            {
                bool noDamageRun = !roundData.SubWasDamaged && !roundData.Casualties.Any(c => !(c.AIController is EnemyAIController));

#if SERVER
                if (GameMain.Server != null)
                {
                    //in MP all characters that were inside the sub during reactor meltdown and still alive at the end of the round get an achievement
                    UnlockAchievement("survivereactormeltdown", true, c => c != null && !c.IsDead && roundData.ReactorMeltdown.Contains(c));
                    if (noDamageRun)
                    {
                        UnlockAchievement("nodamagerun", true, c => c != null && !c.IsDead);                    
                    }
                }
#endif
#if CLIENT
                if (noDamageRun) { UnlockAchievement("nodamagerun"); }
                if (roundData.ReactorMeltdown.Any()) //in SP getting to the destination after a meltdown is enough
                {
                    UnlockAchievement("survivereactormeltdown");
                }
#endif
                var charactersInSub = Character.CharacterList.FindAll(c => 
                    !c.IsDead && 
                    c.TeamID != Character.TeamType.FriendlyNPC &&
                    !(c.AIController is EnemyAIController) &&
                    (c.Submarine == gameSession.Submarine || (Level.Loaded?.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost)));

                if (charactersInSub.Count == 1)
                {
                    //there must be some non-enemy casualties to get the last mant standing achievement
                    if (roundData.Casualties.Any(c => !(c.AIController is EnemyAIController) && c.TeamID == charactersInSub[0].TeamID))
                    {
                        UnlockAchievement(charactersInSub[0], "lastmanstanding");
                    }
#if CLIENT
                    else if (GameMain.GameSession.CrewManager.GetCharacters().Count() == 1)
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor");
                    }
#else
                    //lone sailor achievement if alone in the sub and there are no other characters with the same team ID
                    else if (!Character.CharacterList.Any(c => 
                        c != charactersInSub[0] && 
                        c.TeamID == charactersInSub[0].TeamID && 
                        !(c.AIController is EnemyAIController)))
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor");
                    }
#endif

                }
                foreach (Character character in charactersInSub)
                {
                    if (character.Info.Job == null) { continue; }
                    UnlockAchievement(character, character.Info.Job.Prefab.Identifier + "round");
                }
            }
        }

        private static void UnlockAchievement(Character recipient, string identifier)
        {
            if (CheatsEnabled) return;
            if (recipient == null) return;
#if CLIENT
            if (recipient == Character.Controlled)
            {
                UnlockAchievement(identifier);
            }
#endif
#if SERVER
            GameMain.Server?.GiveAchievement(recipient, identifier);
#endif
        }
        
        public static void UnlockAchievement(string identifier, bool unlockClients = false, Func<Character, bool> conditions = null)
        {
            if (CheatsEnabled) return;
            identifier = identifier.ToLowerInvariant();
            
#if SERVER

            if (unlockClients && GameMain.Server != null)
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (conditions != null && !conditions(c.Character)) continue;
                    GameMain.Server.GiveAchievement(c, identifier);
                }
            }
#endif

            //already unlocked, no need to do anything
            if (unlockedAchievements.Contains(identifier)) return;
            unlockedAchievements.Add(identifier);

#if CLIENT
            if (conditions != null && !conditions(Character.Controlled)) return;
#endif

            SteamManager.UnlockAchievement(identifier);
        }
    }
}
