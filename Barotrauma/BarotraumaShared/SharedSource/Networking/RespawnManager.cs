using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class RespawnManager : Entity, IServerSerializable
    {
        /// <summary>
        /// How much skills drop towards the job's default skill levels when dying
        /// </summary>
        public static float SkillLossPercentageOnDeath => GameMain.NetworkMember?.ServerSettings?.SkillLossPercentageOnDeath ?? 20.0f;

        /// <summary>
        /// How much more the skills drop towards the job's default skill levels
        /// when dying, in addition to SkillLossPercentageOnDeath, if the player
        /// chooses to respawn in the middle of the round
        /// </summary>
        public static float SkillLossPercentageOnImmediateRespawn => GameMain.NetworkMember?.ServerSettings?.SkillLossPercentageOnImmediateRespawn ?? 10.0f;

        public static bool UseDeathPrompt
        {
            get
            {
                return GameMain.GameSession?.GameMode is CampaignMode && Level.Loaded != null;
            }
        }

        public enum State
        {
            Waiting,
            Transporting,
            Returning
        }

        private readonly NetworkMember networkMember;
        private readonly Dictionary<CharacterTeamType, List<Steering>> shuttleSteering = new Dictionary<CharacterTeamType, List<Steering>>();
        private readonly Dictionary<CharacterTeamType, List<Door>> shuttleDoors = new Dictionary<CharacterTeamType, List<Door>>();
        private readonly Dictionary<CharacterTeamType, List<ItemContainer>> respawnContainers = new Dictionary<CharacterTeamType, List<ItemContainer>>();

        private class TeamSpecificState
        {
            public readonly CharacterTeamType TeamID;

            public State State;
            public readonly List<Character> RespawnedCharacters = new List<Character>();
            /// <summary>
            /// When will the shuttle be dispatched with respawned characters
            /// </summary>
            public DateTime RespawnTime;
            /// <summary>
            /// When will the sub start heading back out of the level
            /// </summary>
            public DateTime ReturnTime;
            public DateTime DespawnTime;
            public bool RespawnCountdownStarted;
            public bool ReturnCountdownStarted;

            public int PendingRespawnCount, RequiredRespawnCount;
            public int PrevPendingRespawnCount, PrevRequiredRespawnCount;

            public State CurrentState;

            //items created during respawn
            //any respawn items left in the shuttle are removed when the shuttle despawns
            public readonly List<Item> RespawnItems = new List<Item>();

            public TeamSpecificState(CharacterTeamType teamID)
            {
                TeamID = teamID;
            }
        }

        private readonly Dictionary<CharacterTeamType, TeamSpecificState> teamSpecificStates = new Dictionary<CharacterTeamType, TeamSpecificState>();

        public bool UsingShuttle
        {
            get { return respawnShuttles.Any(); }
        }

        private float maxTransportTime;

        private float updateReturnTimer;

        public bool CanRespawnAgain(CharacterTeamType team)
        {
            if (teamSpecificStates.TryGetValue(team, out var state))
            {
                return state.CurrentState == State.Transporting && maxTransportTime <= 0.0f;
            }
            return false;
        }

        private Dictionary<CharacterTeamType, Submarine> respawnShuttles = new Dictionary<CharacterTeamType, Submarine>();

        public IEnumerable<Submarine> RespawnShuttles => respawnShuttles.Values;

        public RespawnManager(NetworkMember networkMember, SubmarineInfo shuttleInfo)
            : base(null, Entity.RespawnManagerID)
        {
            this.networkMember = networkMember;

            teamSpecificStates = new Dictionary<CharacterTeamType, TeamSpecificState>();
            int teamCount = GameMain.GameSession?.GameMode is PvPMode ? 2 : 1;

            respawnShuttles.Clear();
            List<WifiComponent> wifiComponents = new List<WifiComponent>();
            for (int i = 0; i < teamCount; i++)
            {
                var teamId = i == 0 ? CharacterTeamType.Team1 : CharacterTeamType.Team2;
                teamSpecificStates.Add(teamId, new TeamSpecificState(teamId));

                if (shuttleInfo != null && networkMember.ServerSettings is not { RespawnMode: RespawnMode.Permadeath })
                {
                    shuttleDoors.Add(teamId, new List<Door>());
                    shuttleSteering.Add(teamId, new List<Steering>());
                    respawnContainers.Add(teamId, new List<ItemContainer>());

                    var respawnShuttle = new Submarine(shuttleInfo, true);
                    if (teamId == CharacterTeamType.Team2)
                    {
                        respawnShuttle.FlipX();
                    }

                    respawnShuttles.Add(teamId, respawnShuttle);
                    respawnShuttle.PhysicsBody.FarseerBody.OnCollision += OnShuttleCollision;
                    //set crush depth slightly deeper than the main sub's
                    if (Submarine.MainSub != null)
                    {
                        respawnShuttle.SetCrushDepth(Math.Max(respawnShuttle.RealWorldCrushDepth, Submarine.MainSub.RealWorldCrushDepth * 1.2f));
                    }

                    //prevent wifi components from communicating between the respawn shuttle and other subs
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Submarine == respawnShuttle) { wifiComponents.AddRange(item.GetComponents<WifiComponent>()); }
                    }
                    foreach (WifiComponent wifiComponent in wifiComponents)
                    {
                        wifiComponent.TeamID = CharacterTeamType.FriendlyNPC;
                    }

                    ResetShuttle(teamSpecificStates[teamId]);

                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Submarine != respawnShuttle) { continue; }

                        if (item.HasTag(Tags.RespawnContainer))
                        {
                            if (GameMain.GameSession?.Missions != null)
                            {
                                foreach (var mission in GameMain.GameSession.Missions)
                                {
                                    //append the mission type so respawn gear can be configured per mission type (e.g. respawncontainer_kingofthehull)
                                    item.AddTag(Tags.RespawnContainer.AppendIfMissing("_" + mission.Prefab.Type));
                                }
                            }
                            respawnContainers[teamId].Add(item.GetComponent<ItemContainer>());
                        }

                        var steering = item.GetComponent<Steering>();
                        if (steering != null) { shuttleSteering[teamId].Add(steering); }

                        var door = item.GetComponent<Door>();
                        if (door != null) { shuttleDoors[teamId].Add(door); }

                        //lock all wires to prevent the players from messing up the electronics
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        if (connectionPanel != null)
                        {
                            foreach (Connection connection in connectionPanel.Connections)
                            {
                                foreach (Wire wire in connection.Wires)
                                {
                                    if (wire != null) wire.Locked = true;
                                }
                            }
                        }
                    }
                }
            }

#if SERVER
            if (networkMember is GameServer server)
            {
                maxTransportTime = server.ServerSettings.MaxTransportTime;
            }
#endif
        }

        private bool OnShuttleCollision(Fixture sender, Fixture other, Contact contact)
        {
            if (sender.Body.UserData is not Submarine sub || !teamSpecificStates.ContainsKey(sub.TeamID)) { return true; }
            //ignore collisions with the top barrier when returning
            return teamSpecificStates[sub.TeamID].CurrentState != State.Returning || other?.Body != Level.Loaded?.TopBarrier;
        }

        public void Update(float deltaTime)
        {
            foreach (var teamSpecificState in teamSpecificStates.Values)
            {
                if (RespawnShuttles.None())
                {
                    if (teamSpecificState.CurrentState != State.Waiting)
                    {
                        teamSpecificState.CurrentState = State.Waiting;
                    }
                }
                switch (teamSpecificState.CurrentState)
                {
                    case State.Waiting:
                        UpdateWaiting(teamSpecificState);
                        break;
                    case State.Transporting:
                        UpdateTransporting(teamSpecificState, deltaTime);
                        break;
                    case State.Returning:
                        UpdateReturning(teamSpecificState, deltaTime);
                        break;
                }
            }
        }

        partial void UpdateWaiting(TeamSpecificState teamSpecificState);
        
        private void UpdateTransporting(TeamSpecificState teamSpecificState, float deltaTime)
        {
            //infinite transport time -> shuttle wont return
            if (maxTransportTime <= 0.0f) return;
            UpdateTransportingProjSpecific(teamSpecificState, deltaTime);
        }

        partial void UpdateTransportingProjSpecific(TeamSpecificState teamSpecificState, float deltaTime);

        public void ForceRespawn()
        {
            foreach (var teamSpecificState in teamSpecificStates.Values)
            {
                if (teamSpecificState.CurrentState == State.Transporting) { continue; }
                ResetShuttle(teamSpecificState);
                teamSpecificState.RespawnCountdownStarted = true;
                teamSpecificState.RespawnTime = DateTime.Now;
                teamSpecificState.CurrentState = State.Waiting;
            }
        }

        private void UpdateReturning(TeamSpecificState teamSpecificState, float deltaTime)
        {
            updateReturnTimer += deltaTime;
            if (updateReturnTimer > 1.0f)
            {
                updateReturnTimer = 0.0f;
                shuttleSteering[teamSpecificState.TeamID].ForEach(steering => steering.SetDestinationLevelStart());
                UpdateReturningProjSpecific(teamSpecificState, deltaTime);
            }
        }

        partial void UpdateReturningProjSpecific(TeamSpecificState teamSpecificState, float deltaTime);

        private Submarine GetShuttle(CharacterTeamType team)
        {
            if (respawnShuttles.TryGetValue(team, out Submarine sub))
            {
                return sub;
            }
            return null;
        }

        private void ResetShuttle(TeamSpecificState teamSpecificState)
        {
            teamSpecificState.ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(maxTransportTime * 1000));

#if SERVER
            teamSpecificState.DespawnTime = teamSpecificState.ReturnTime + new TimeSpan(0, 0, seconds: 30);
#endif
            var shuttle = GetShuttle(teamSpecificState.TeamID);
            if (shuttle == null) { return; }

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != shuttle) { continue; }
                
                //remove respawn items that have been left in the shuttle
                if (teamSpecificState.RespawnItems.Contains(item) ||
                    respawnContainers[teamSpecificState.TeamID].Any(container => item.IsOwnedBy(container.Item)))
                {
                    Spawner.AddItemToRemoveQueue(item);
                    continue;
                }

#if CLIENT
                foreach (var itemComponent in item.Components)
                {
                    itemComponent.StopLoopingSound();
                }
#endif

                //restore other items to full condition and recharge batteries
                item.Condition = item.MaxCondition;
                item.GetComponent<Repairable>()?.ResetDeterioration();
                var powerContainer = item.GetComponent<PowerContainer>();
                if (powerContainer != null)
                {
                    powerContainer.Charge = powerContainer.GetCapacity();
                }

                var door = item.GetComponent<Door>();
                if (door != null) { door.Stuck = 0.0f; }

                var steering = item.GetComponent<Steering>();
                if (steering != null)
                {
                    steering.MaintainPos = true;
                    steering.AutoPilot = true;
#if SERVER
                    steering.UnsentChanges = true;
#endif
                }
            }
            teamSpecificState.RespawnItems.Clear();

            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != shuttle) { continue; }
                for (int i = 0; i < wall.SectionCount; i++)
                {
                    wall.AddDamage(i, -100000.0f);
                }            
            }

            foreach (Hull hull in Hull.HullList)
            {
                if (hull.Submarine != shuttle) { continue; }
                hull.OxygenPercentage = 100.0f;
                hull.WaterVolume = 0.0f;
                hull.BallastFlora?.Remove();
            }

            Dictionary<Character, Vector2> characterPositions = new Dictionary<Character, Vector2>();
            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine != shuttle) { continue; }
                if (!teamSpecificState.RespawnedCharacters.Contains(c)) 
                {
                    characterPositions.Add(c, c.WorldPosition);
                    continue; 
                }
#if CLIENT
                if (Character.Controlled == c) { Character.Controlled = null; }
#endif
                c.Kill(CauseOfDeathType.Unknown, null, true);
                c.Enabled = false;
                
                Spawner.AddEntityToRemoveQueue(c);
                if (c.Inventory != null)
                {
                    foreach (Item item in c.Inventory.AllItems)
                    {
                        Spawner.AddItemToRemoveQueue(item);
                    }
                }
            }

            shuttle.SetPosition(new Vector2(
                teamSpecificState.TeamID == CharacterTeamType.Team1 ? Level.Loaded.StartPosition.X : Level.Loaded.EndPosition.X, 
                Level.Loaded.Size.Y + shuttle.Borders.Height));
            shuttle.Velocity = Vector2.Zero;            

            foreach (var characterPosition in characterPositions)
            {
                characterPosition.Key.TeleportTo(characterPosition.Value);
            }
        }

        public static float GetReducedSkill(CharacterInfo characterInfo, Skill skill, float skillLossPercentage, float? currentSkillLevel = null)
        {
            var skillPrefab = characterInfo.Job.Prefab.Skills.Find(s => skill.Identifier == s.Identifier);
            float currentLevel = currentSkillLevel ?? skill.Level;
            if (skillPrefab == null) { return currentLevel; }
            var levelRange = skillPrefab.GetLevelRange(isPvP: GameMain.GameSession?.GameMode is PvPMode);
            if (currentLevel < levelRange.End) { return currentLevel; }
            return MathHelper.Lerp(currentLevel, levelRange.End, skillLossPercentage / 100.0f);
        }

        partial void RespawnCharactersProjSpecific(Vector2? shuttlePos);
        public void RespawnCharacters(Vector2? shuttlePos)
        {
            RespawnCharactersProjSpecific(shuttlePos);
        }

        public static AfflictionPrefab GetRespawnPenaltyAfflictionPrefab()
        {
            return AfflictionPrefab.Prefabs.First(a => a.AfflictionType == "respawnpenalty");
        }

        public static Affliction GetRespawnPenaltyAffliction()
        {
            return GetRespawnPenaltyAfflictionPrefab()?.Instantiate(10.0f);
        }

        public static void GiveRespawnPenaltyAffliction(Character character)
        {
            var respawnPenaltyAffliction = GetRespawnPenaltyAffliction();
            if (respawnPenaltyAffliction != null)
            {
                character.CharacterHealth.ApplyAffliction(targetLimb: null, respawnPenaltyAffliction);
            }
        }

        public Vector2 FindSpawnPos(Submarine respawnShuttle, Submarine mainSub)
        {
            if (Level.Loaded == null || Submarine.MainSub == null) { return Vector2.Zero; }

            Rectangle dockedBorders = respawnShuttle.GetDockedBorders();
            Vector2 diffFromDockedBorders =
                new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)
                - new Vector2(respawnShuttle.Borders.Center.X, respawnShuttle.Borders.Y - respawnShuttle.Borders.Height / 2);

            int minWidth = Math.Max(dockedBorders.Width, 1000);
            int minHeight = Math.Max(dockedBorders.Height, 1000);

            List<Level.InterestingPosition> potentialSpawnPositions = FindValidSpawnPoints(respawnShuttle, minWidth, minHeight, minDistFromSubs: 10000.0f, minDistFromCharacters: 5000.0f);
            if (potentialSpawnPositions.None())
            {
                DebugConsole.NewMessage("Failed to find a shuttle spawn position far away from submarines and characters, attempting to find one closer to to subs and characters...");
                potentialSpawnPositions = FindValidSpawnPoints(respawnShuttle, minWidth, minHeight, minDistFromSubs: 1000.0f, minDistFromCharacters: 500.0f);
                if (potentialSpawnPositions.None())
                {
                    DebugConsole.NewMessage("Failed to find a shuttle spawn position, using the level's start position instead.");
                    return Level.Loaded.StartPosition;
                }
            }

            Vector2 bestSpawnPos = Level.Loaded.StartPosition;
            float bestSpawnPosValue = 0.0f;
            foreach (var potentialSpawnPos in potentialSpawnPositions)
            {
                //the closer the spawnpos is to the main sub, the better
                float spawnPosValue = 100000.0f / Math.Max(Vector2.Distance(potentialSpawnPos.Position.ToVector2(), mainSub.WorldPosition), 1.0f);

                //prefer spawnpoints that are at the left side of the sub (so the shuttle doesn't have to go backwards)
                if (potentialSpawnPos.Position.X > mainSub.WorldPosition.X)
                {
                    spawnPosValue *= 0.1f;
                }

                if (spawnPosValue > bestSpawnPosValue)
                {
                    bestSpawnPos = potentialSpawnPos.Position.ToVector2();
                    bestSpawnPosValue = spawnPosValue;
                }
            }

            return bestSpawnPos;
        }

        private List<Level.InterestingPosition> FindValidSpawnPoints(Submarine respawnShuttle, float minWidth, float minHeight, float minDistFromSubs, float minDistFromCharacters)
        {
            List<Level.InterestingPosition> potentialSpawnPositions = new List<Level.InterestingPosition>();
            foreach (Level.InterestingPosition potentialSpawnPos in Level.Loaded.PositionsOfInterest.Where(p => p.PositionType == Level.PositionType.MainPath))
            {
                bool invalid = false;
                //make sure the shuttle won't overlap with any ruins
                foreach (var ruin in Level.Loaded.Ruins)
                {
                    if (Math.Abs(ruin.Area.Center.X - potentialSpawnPos.Position.X) < (minWidth + ruin.Area.Width) / 2) { invalid = true; break; }
                    if (Math.Abs(ruin.Area.Center.Y - potentialSpawnPos.Position.Y) < (minHeight + ruin.Area.Height) / 2) { invalid = true; break; }
                }
                if (invalid) { continue; }

                //make sure there aren't any walls too close
                var tooCloseCells = Level.Loaded.GetTooCloseCells(potentialSpawnPos.Position.ToVector2(), Math.Max(minWidth, minHeight));
                if (tooCloseCells.Any()) { continue; }

                //make sure the spawnpoint is far enough from other subs
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub == respawnShuttle || respawnShuttle.DockedTo.Contains(sub)) { continue; }
                    float minDist = Math.Max(Math.Max(minWidth, minHeight) + Math.Max(sub.Borders.Width, sub.Borders.Height), minDistFromSubs);
                    if (Vector2.DistanceSquared(sub.WorldPosition, potentialSpawnPos.Position.ToVector2()) < minDist * minDist)
                    {
                        invalid = true;
                        break;
                    }
                }
                if (invalid) { continue; }

                foreach (Character character in Character.CharacterList)
                {
                    if (character.IsDead)
                    {
                        //cannot spawn directly over dead bodies
                        if (Math.Abs(character.WorldPosition.X - potentialSpawnPos.Position.X) < minWidth) { invalid = true; break; }
                        if (Math.Abs(character.WorldPosition.Y - potentialSpawnPos.Position.Y) < minHeight) { invalid = true; break; }
                    }
                    else
                    {
                        //cannot spawn near alive characters (to prevent other players from seeing the shuttle 
                        //appear out of nowhere, or monsters from immediatelly wrecking the shuttle)
                        if (Vector2.DistanceSquared(character.WorldPosition, potentialSpawnPos.Position.ToVector2()) < minDistFromCharacters * minDistFromCharacters)
                        {
                            invalid = true;
                            break;
                        }
                    }
                }
                if (invalid) { continue; }

                potentialSpawnPositions.Add(potentialSpawnPos);
            }
            return potentialSpawnPositions;
        }
    }
}
