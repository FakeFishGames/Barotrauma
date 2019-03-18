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
            if (GameMain.GameSession == null || GameMain.Client != null) return;

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
                    if (c.IsDead) continue;
                    //achievement for descending below crush depth and coming back
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
                    if (realWorldDepth > 5000.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subdeep", true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }
                }
            }                 
        }

        public static void OnBiomeDiscovered(Biome biome)
        {
            UnlockAchievement("discover" + biome.Name.ToLowerInvariant().Replace(" ", ""));
        }

        public static void OnItemRepaired(Item item, Character fixer)
        {
            if (GameMain.Client != null || fixer == null) return;
            
            UnlockAchievement(fixer, "repairdevice");
            UnlockAchievement(fixer, "repair" + item.Prefab.Identifier);
        }

        public static void OnAfflictionRemoved(Affliction affliction, Character character)
        {
            if (string.IsNullOrEmpty(affliction.Prefab.AchievementOnRemoved)) return;

            if (GameMain.Client != null) return;            
            UnlockAchievement(character, affliction.Prefab.AchievementOnRemoved);
        }

        public static void OnCharacterRevived(Character character, Character reviver)
        {
            if (reviver == null || GameMain.Client != null) return;
            UnlockAchievement(reviver, "healcrit");
        }

        public static void OnCharacterKilled(Character character, CauseOfDeath causeOfDeath)
        {
            if (character != Character.Controlled &&
                causeOfDeath.Killer != null && 
                causeOfDeath.Killer == Character.Controlled)
            {
                SteamManager.IncrementStat(
                    character.SpeciesName.ToLowerInvariant() == "human" ? "humanskilled" : "monsterskilled", 
                    1);
            }

            if (GameMain.Client != null || GameMain.GameSession == null) return;

            roundData.Casualties.Add(character);

            UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName);
            if (character.CurrentHull != null)
            {
                UnlockAchievement(causeOfDeath.Killer, "kill" + character.SpeciesName + "indoors");
            }

            if (character.HasEquippedItem("clownmask") && 
                character.HasEquippedItem("clowncostume"))
            {
                UnlockAchievement(causeOfDeath.Killer, "killclown");
            }

            if (causeOfDeath.DamageSource is Item item)
            {
                switch (item.Prefab.Identifier)
                {
                    case "weldingtool":
                    case "plasmacutter":
                    case "wrench":
                        UnlockAchievement(causeOfDeath.Killer, "killtool");
                        break;
                    case "morbusine":
                        UnlockAchievement(causeOfDeath.Killer, "killpoison");
                        break;
                    case "nuclearshell":
                    case "nucleardepthcharge":
                        UnlockAchievement(causeOfDeath.Killer, "killnuke");
                        break;
                }
            }

            if (GameMain.Server?.TraitorManager != null)
            {
                foreach (Traitor traitor in GameMain.Server.TraitorManager.TraitorList)
                {
                    if (traitor.TargetCharacter == character)
                    {
                        //killed the target as a traitor
                        UnlockAchievement(traitor.Character, "traitorwin");
                    }
                    else if (traitor.Character == character)
                    {
                        //someone killed a traitor
                        UnlockAchievement(causeOfDeath.Killer, "killtraitor");
                    }
                }
            }
        }

        public static void OnRoundEnded(GameSession gameSession)
        {
            //made it to the destination
            if (gameSession.Submarine.AtEndPosition && Level.Loaded != null)
            {
                float levelLengthMeters = Physics.DisplayToRealWorldRatio * Level.Loaded.Size.X;
                float levelLengthKilometers = levelLengthMeters / 1000.0f;
                //in multiplayer the client's/host's character must be inside the sub (or end outpost) and alive
                if (GameMain.NetworkMember != null)
                {
                    Character myCharacter = GameMain.NetworkMember.Character;
                    if (myCharacter != null &&
                        !myCharacter.IsDead &&
                        (myCharacter.Submarine == gameSession.Submarine || (Level.Loaded?.EndOutpost != null && myCharacter.Submarine == Level.Loaded.EndOutpost)))
                    {
                        SteamManager.IncrementStat("kmstraveled", levelLengthKilometers);
                    }
                }
                else
                {
                    //in sp making it to the end is enough
                    SteamManager.IncrementStat("kmstraveled", levelLengthKilometers);
                }
            }

            if (GameMain.Client != null) { return; }

            if (gameSession.Mission != null)
            {
                if (gameSession.Mission is CombatMission combatMission)
                {
                    //all characters that are alive and in the winning team get an achievement
                    UnlockAchievement(gameSession.Mission.Prefab.AchievementIdentifier + combatMission.Winner, true, 
                        c => c != null && !c.IsDead && !c.IsUnconscious && combatMission.IsInWinningTeam(c));
                }
                else if (gameSession.Mission.Completed)
                {
                    //all characters get an achievement
                    if (GameMain.Server != null)
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
                if (GameMain.Server != null)
                {
                    //in MP all characters that were inside the sub during reactor meltdown and still alive at the end of the round get an achievement
                    UnlockAchievement("survivereactormeltdown", true, c => c != null && !c.IsDead && roundData.ReactorMeltdown.Contains(c));
                }
                else if (roundData.ReactorMeltdown.Any()) //in SP getting to the destination after a meltdown is enough
                {
                    UnlockAchievement("survivereactormeltdown");
                }

                var charactersInSub = Character.CharacterList.FindAll(c => !c.IsDead &&
                    (c.Submarine == gameSession.Submarine || (Level.Loaded?.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost)));
                if (charactersInSub.Count == 1)
                {
                    //there must be some non-enemy casualties to get the last mant standing achievement
                    if (roundData.Casualties.Any(c => !(c.AIController is EnemyAIController)))
                    {
                        UnlockAchievement(charactersInSub[0], "lastmanstanding");
                    }
                    else if (!Character.CharacterList.Any(c => !(c.AIController is EnemyAIController)))
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor");
                    }
                }
            }
        }

        private static void UnlockAchievement(Character recipient, string identifier)
        {
            if (CheatsEnabled) return;
            if (recipient == null) return;
            if (recipient == Character.Controlled)
            {
                UnlockAchievement(identifier);
            }
            else
            {
                GameMain.Server?.GiveAchievement(recipient, identifier);
            }
        }
        
        public static void UnlockAchievement(string identifier, bool unlockClients = false, Func<Character, bool> conditions = null)
        {
            if (CheatsEnabled) return;

            identifier = identifier.ToLowerInvariant();

            if (unlockClients && GameMain.Server != null)
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (conditions != null && !conditions(c.Character)) continue;
                    GameMain.Server.GiveAchievement(c, identifier);
                }
            }

            //already unlocked, no need to do anything
            if (unlockedAchievements.Contains(identifier)) return;
            unlockedAchievements.Add(identifier);

            if (conditions != null && !conditions(Character.Controlled)) return;

            SteamManager.UnlockAchievement(identifier);
        }
    }
}
