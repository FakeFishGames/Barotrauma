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
        public enum State
        {
            Waiting,
            Transporting,
            Returning
        }

        private readonly NetworkMember networkMember;
        private readonly Steering shuttleSteering;
        private readonly List<Door> shuttleDoors;
        private const string RespawnContainerTag = "respawncontainer";
        private readonly ItemContainer respawnContainer;

        //items created during respawn
        //any respawn items left in the shuttle are removed when the shuttle despawns
        private readonly List<Item> respawnItems = new List<Item>();

        //characters who spawned during the last respawn
        private readonly List<Character> respawnedCharacters = new List<Character>();

        public bool UsingShuttle
        {
            get { return RespawnShuttle != null; }
        }

        /// <summary>
        /// When will the shuttle be dispatched with respawned characters
        /// </summary>
        public DateTime RespawnTime { get; private set; }

        /// <summary>
        /// When will the sub start heading back out of the level
        /// </summary>
        public DateTime ReturnTime { get; private set; }

        public bool RespawnCountdownStarted
        {
            get;
            private set;
        }

        public bool ReturnCountdownStarted
        {
            get;
            private set;
        }

        public State CurrentState { get; private set; }

        public bool UseRespawnPrompt
        {
            get
            {
                return GameMain.GameSession?.GameMode is CampaignMode && Level.Loaded != null && Level.Loaded?.Type != LevelData.LevelType.Outpost;
            }
        }

        private float maxTransportTime;

        private float updateReturnTimer;

        public Submarine RespawnShuttle { get; private set; }

        public RespawnManager(NetworkMember networkMember, SubmarineInfo shuttleInfo)
            : base(null, Entity.RespawnManagerID)
        {
            this.networkMember = networkMember;

            if (shuttleInfo != null)
            {
                RespawnShuttle = new Submarine(shuttleInfo, true);
                RespawnShuttle.PhysicsBody.FarseerBody.OnCollision += OnShuttleCollision;
                //set crush depth slightly deeper than the main sub's
                RespawnShuttle.RealWorldCrushDepth = Math.Max(RespawnShuttle.RealWorldCrushDepth, Submarine.MainSub.RealWorldCrushDepth * 1.2f);

                //prevent wifi components from communicating between the respawn shuttle and other subs
                List<WifiComponent> wifiComponents = new List<WifiComponent>();
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == RespawnShuttle) { wifiComponents.AddRange(item.GetComponents<WifiComponent>()); }                   
                }
                foreach (WifiComponent wifiComponent in wifiComponents)
                {
                    wifiComponent.TeamID = CharacterTeamType.FriendlyNPC;
                }

                ResetShuttle();
                
                shuttleDoors = new List<Door>();
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine != RespawnShuttle) { continue; }

                    if (item.HasTag(RespawnContainerTag))
                    {
                        respawnContainer = item.GetComponent<ItemContainer>();
                    }

                    var steering = item.GetComponent<Steering>();
                    if (steering != null) { shuttleSteering = steering; }

                    var door = item.GetComponent<Door>();
                    if (door != null) { shuttleDoors.Add(door); }

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
            else
            {
                RespawnShuttle = null;
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
            //ignore collisions with the top barrier when returning
            return CurrentState != State.Returning || other?.Body != Level.Loaded?.TopBarrier;
        }

        public void Update(float deltaTime)
        {
            if (RespawnShuttle == null)
            {
                if (CurrentState != State.Waiting)
                {
                    CurrentState = State.Waiting;
                }
            }

            switch (CurrentState)
            {
                case State.Waiting:
                    UpdateWaiting(deltaTime);
                    break;
                case State.Transporting:
                    UpdateTransporting(deltaTime);
                    break;
                case State.Returning:
                    UpdateReturning(deltaTime);
                    break;
            }
        }

        partial void UpdateWaiting(float deltaTime);
        
        private void UpdateTransporting(float deltaTime)
        {
            //infinite transport time -> shuttle wont return
            if (maxTransportTime <= 0.0f) return;
            UpdateTransportingProjSpecific(deltaTime);
        }

        partial void UpdateTransportingProjSpecific(float deltaTime);

        public void ForceRespawn()
        {
            ResetShuttle();
            RespawnTime = DateTime.Now;
            CurrentState = State.Waiting;
        }

        private void UpdateReturning(float deltaTime)
        {
            updateReturnTimer += deltaTime;
            if (updateReturnTimer > 1.0f)
            {
                updateReturnTimer = 0.0f;
                shuttleSteering?.SetDestinationLevelStart();
                UpdateReturningProjSpecific(deltaTime);
            }
        }

        partial void UpdateReturningProjSpecific(float deltaTime);
        
        private IEnumerable<CoroutineStatus> ForceShuttleToPos(Vector2 position, float speed)
        {
            if (RespawnShuttle == null)
            {
                yield return CoroutineStatus.Success;
            }

            while (Math.Abs(position.Y - RespawnShuttle.WorldPosition.Y) > 100.0f)
            {
                Vector2 diff = position - RespawnShuttle.WorldPosition;
                if (diff.LengthSquared() > 0.01f)
                {
                    Vector2 displayVel = Vector2.Normalize(diff) * speed;
                    RespawnShuttle.SubBody.Body.LinearVelocity = ConvertUnits.ToSimUnits(displayVel);
                }
                yield return CoroutineStatus.Running;

                if (RespawnShuttle.SubBody == null) yield return CoroutineStatus.Success;
            }

            yield return CoroutineStatus.Success;
        }

        private void ResetShuttle()
        {
            ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(maxTransportTime * 1000));

#if SERVER
            despawnTime = ReturnTime + new TimeSpan(0, 0, seconds: 30);
#endif

            if (RespawnShuttle == null) { return; }

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != RespawnShuttle) { continue; }
                
                //remove respawn items that have been left in the shuttle
                if (respawnItems.Contains(item) || respawnContainer?.Item != null && item.IsOwnedBy(respawnContainer.Item))
                {
                    Spawner.AddItemToRemoveQueue(item);
                    continue;
                }

                //restore other items to full condition and recharge batteries
                item.Condition = item.MaxCondition;
                item.GetComponent<Repairable>()?.ResetDeterioration();
                var powerContainer = item.GetComponent<PowerContainer>();
                if (powerContainer != null)
                {
                    powerContainer.Charge = powerContainer.Capacity;
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

            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != RespawnShuttle) { continue; }
                for (int i = 0; i < wall.SectionCount; i++)
                {
                    wall.AddDamage(i, -100000.0f);
                }            
            }

            foreach (Hull hull in Hull.HullList)
            {
                if (hull.Submarine != RespawnShuttle) { continue; }
                hull.OxygenPercentage = 100.0f;
                hull.WaterVolume = 0.0f;
                hull.BallastFlora?.Kill();
            }

            Dictionary<Character, Vector2> characterPositions = new Dictionary<Character, Vector2>();
            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine != RespawnShuttle) { continue; }
                if (!respawnedCharacters.Contains(c)) 
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

            RespawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + RespawnShuttle.Borders.Height));
            RespawnShuttle.Velocity = Vector2.Zero;

            foreach (var characterPosition in characterPositions)
            {
                characterPosition.Key.TeleportTo(characterPosition.Value);
            }
        }

        partial void RespawnCharactersProjSpecific(Vector2? shuttlePos);
        public void RespawnCharacters(Vector2? shuttlePos)
        {
            RespawnCharactersProjSpecific(shuttlePos);
        }

        public static Affliction GetRespawnPenaltyAffliction()
        {
            var respawnPenaltyAffliction = AfflictionPrefab.Prefabs.First(a => a.AfflictionType == "respawnpenalty");
            return respawnPenaltyAffliction?.Instantiate(10.0f);
        }

        public static void GiveRespawnPenaltyAffliction(Character character)
        {
            var respawnPenaltyAffliction = GetRespawnPenaltyAffliction();
            if (respawnPenaltyAffliction != null)
            {
                character.CharacterHealth.ApplyAffliction(targetLimb: null, respawnPenaltyAffliction);
            }
        }

        public Vector2 FindSpawnPos()
        {
            if (Level.Loaded == null || Submarine.MainSub == null) { return Vector2.Zero; }

            Rectangle dockedBorders = RespawnShuttle.GetDockedBorders();
            Vector2 diffFromDockedBorders =
                new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)
                - new Vector2(RespawnShuttle.Borders.Center.X, RespawnShuttle.Borders.Y - RespawnShuttle.Borders.Height / 2);

            int minWidth = Math.Max(dockedBorders.Width, 1000);
            int minHeight = Math.Max(dockedBorders.Height, 1000);

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
                    if (sub == RespawnShuttle || RespawnShuttle.DockedTo.Contains(sub)) { continue; }
                    float minDist = Math.Max(Math.Max(minWidth, minHeight) + Math.Max(sub.Borders.Width, sub.Borders.Height), 10000.0f);
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
                        if (Vector2.DistanceSquared(character.WorldPosition, potentialSpawnPos.Position.ToVector2()) < 5000.0f * 5000.0f)
                        {
                            invalid = true;
                            break;
                        }
                    }
                }
                if (invalid) { continue; }

                potentialSpawnPositions.Add(potentialSpawnPos);
            }
            Vector2 bestSpawnPos = new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + RespawnShuttle.Borders.Height);
            float bestSpawnPosValue = 0.0f;
            foreach (var potentialSpawnPos in potentialSpawnPositions)
            {
                //the closer the spawnpos is to the main sub, the better
                float spawnPosValue = 100000.0f / Math.Max(Vector2.Distance(potentialSpawnPos.Position.ToVector2(), Submarine.MainSub.WorldPosition), 1.0f);

                //prefer spawnpoints that are at the left side of the sub (so the shuttle doesn't have to go backwards)
                if (potentialSpawnPos.Position.X > Submarine.MainSub.WorldPosition.X)
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
    }
}
