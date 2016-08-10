using Barotrauma.Items.Components;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class RespawnManager
    {
        private readonly float respawnInterval;
        private float maxTransportTime;
        
        public enum State
        {
            Waiting,
            Transporting,
            Returning
        }

        private NetworkMember networkMember;

        private State state;
        
        private Submarine respawnShuttle;
        private Steering shuttleSteering;
        private List<Door> shuttleDoors;

        /// <summary>
        /// How long until the shuttle is dispatched with respawned characters
        /// </summary>
        public float RespawnTimer
        {
            get { return respawnTimer; }
        }

        /// <summary>
        /// how long until the shuttle starts heading back out of the level
        /// </summary>
        public float TransportTimer
        {
            get { return shuttleTransportTimer; }
        }

        public bool CountdownStarted
        {
            get;
            private set;
        }

        public State CurrentState
        {
            get { return state; }
        }

        private float respawnTimer, shuttleReturnTimer, shuttleTransportTimer;

        private float updateReturnTimer;

        public RespawnManager(NetworkMember networkMember, Submarine shuttle)
        {
            this.networkMember = networkMember;

            respawnShuttle = shuttle;
            respawnShuttle.Load(false);

            ResetShuttle();

            //respawnShuttle.GodMode = true;
            
            shuttleDoors = new List<Door>();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;

                var steering = item.GetComponent<Steering>();
                if (steering != null) shuttleSteering = steering;

                var door = item.GetComponent<Door>();
                if (door != null) shuttleDoors.Add(door);
            }

            if (shuttleSteering != null)
            {
                shuttleSteering.TargetPosition = ConvertUnits.ToSimUnits(Level.Loaded.StartPosition);
            }

            var server = networkMember as GameServer;
            if (server != null)
            {
                respawnInterval = server.RespawnInterval;
                maxTransportTime = server.MaxTransportTime;
            }            

            respawnTimer = respawnInterval;            
        }
        
        private List<Client> GetClientsToRespawn()
        {
            return networkMember.ConnectedClients.FindAll(c => c.inGame && (c.Character == null || c.Character.IsDead));
        }

        public void Update(float deltaTime)
        {
            switch (state)
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

        private void UpdateWaiting(float deltaTime)
        {
            var server = networkMember as GameServer;
            if (server == null)
            {
                if (CountdownStarted)
                {
                    respawnTimer = Math.Max(0.0f, respawnTimer - deltaTime);
                }
                return;
            }

            respawnShuttle.Velocity = Vector2.Zero;

            shuttleSteering.AutoPilot = false;
            shuttleSteering.MaintainPos = false;

            int characterToRespawnCount = GetClientsToRespawn().Count;
            int totalCharacterCount = server.ConnectedClients.Count;
            if (server.Character != null)
            {
                totalCharacterCount++;
                if (server.Character.IsDead) characterToRespawnCount++;
            }
            bool startCountdown = (float)characterToRespawnCount >= Math.Max((float)totalCharacterCount * server.MinRespawnRatio, 1.0f);

            if (startCountdown)
            {
                if (!CountdownStarted)
                {
                    CountdownStarted = true;
                    server.SendRespawnManagerMsg();
                }
            }
            else
            {
                CountdownStarted = false;
            }

            if (!CountdownStarted) return;

            respawnTimer -= deltaTime;
            if (respawnTimer <= 0.0f)
            {
                respawnTimer = respawnInterval;

                Respawn();
            }
        }

        private void UpdateTransporting(float deltaTime)
        {
            shuttleTransportTimer -= deltaTime;

            if (shuttleReturnTimer + deltaTime > 15.0f && shuttleReturnTimer <= 15.0f &&
                networkMember.Character != null &&
                networkMember.Character.Submarine == respawnShuttle)
            {
                networkMember.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            }

            //infinite transport time -> shuttle wont return
            if (maxTransportTime < 0.1f) return;

            var server = networkMember as GameServer;
            if (server == null) return;


            //if there are no living chracters inside, transporting can be stopped immediately
            if (!Character.CharacterList.Any(c => c.Submarine == respawnShuttle && !c.IsDead))
            {
                shuttleTransportTimer = 0.0f;
            }

            if (shuttleTransportTimer <= 0.0f)
            {
                state = State.Returning;

                server.SendRespawnManagerMsg();
                shuttleReturnTimer = maxTransportTime;
                shuttleTransportTimer = maxTransportTime;
            }
        }

        private void UpdateReturning(float deltaTime)
        {
            //if (shuttleReturnTimer == maxTransportTime && 
            //    networkMember.Character != null && 
            //    networkMember.Character.Submarine == respawnShuttle)
            //{
            //    networkMember.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            //}

            shuttleReturnTimer -= deltaTime;

            updateReturnTimer += deltaTime;

            if (updateReturnTimer > 10.0f)
            {
                updateReturnTimer = 0.0f;
                
                respawnShuttle.SubBody.Body.IgnoreCollisionWith(Level.Loaded.ShaftBodies[0]);

                shuttleSteering.AutoPilot = true;
                shuttleSteering.MaintainPos = false;

                foreach (Door door in shuttleDoors)
                {
                    if (door.IsOpen) door.SetState(false, false, true);
                }

                var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
                shuttleGaps.ForEach(g => g.Remove());

                var dockingPorts = Item.ItemList.FindAll(i => i.Submarine == respawnShuttle && i.GetComponent<DockingPort>() != null);
                dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());

                var server = networkMember as GameServer;
                if (server == null) return;
                
                //shuttle has returned if the path has been traversed or the shuttle is close enough to the exit

                if (!CoroutineManager.IsCoroutineRunning("forcepos"))
                {
                    if (shuttleSteering.SteeringPath != null && shuttleSteering.SteeringPath.Finished
                        || (respawnShuttle.WorldPosition.Y + respawnShuttle.Borders.Y > Level.Loaded.StartPosition.Y - Level.ShaftHeight &&
                            Math.Abs(Level.Loaded.StartPosition.X - respawnShuttle.WorldPosition.X) < 1000.0f))
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                        CoroutineManager.StartCoroutine(
                            ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f), "forcepos");

                    }
                }

                if (respawnShuttle.WorldPosition.Y > Level.Loaded.Size.Y || shuttleReturnTimer <= 0.0f)
                {
                    CoroutineManager.StopCoroutines("forcepos");

                    ResetShuttle();

                    state = State.Waiting;
                    respawnTimer = respawnInterval;

                    server.SendRespawnManagerMsg();
                }
            }
        }

        private void Respawn()
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            state = State.Transporting;

            ResetShuttle();

            shuttleSteering.TargetVelocity = Vector2.Zero;

            server.SendChatMessage(ChatMessage.Create("", "Transportation shuttle dispatched", ChatMessageType.Server, null), server.ConnectedClients);

            server.SendRespawnManagerMsg();

            CoroutineManager.StopCoroutines("forcepos");
            CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
        }

        private IEnumerable<object> ForceShuttleToPos(Vector2 position, float speed)
        {
            respawnShuttle.SubBody.Body.IgnoreCollisionWith(Level.Loaded.ShaftBodies[0]);

            while (Math.Abs(position.Y - respawnShuttle.WorldPosition.Y) > 100.0f)
            {
                Vector2 displayVel = Vector2.Normalize(position - respawnShuttle.WorldPosition) * speed;
                respawnShuttle.SubBody.Body.LinearVelocity = ConvertUnits.ToSimUnits(displayVel);
                yield return CoroutineStatus.Running;

                if (respawnShuttle.SubBody == null) yield return CoroutineStatus.Success;
            }

            respawnShuttle.SubBody.Body.RestoreCollisionWith(Level.Loaded.ShaftBodies[0]);

            yield return CoroutineStatus.Success;
        }

        private void ResetShuttle()
        {
            shuttleTransportTimer = maxTransportTime;
            shuttleReturnTimer = maxTransportTime;

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;

                if (item.body != null && item.body.Enabled && item.ParentInventory == null)
                {
                    Item.Remover.QueueItem(item);
                }
            }

            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != respawnShuttle) continue;

                for (int i = 0; i < wall.SectionCount; i++)
                {
                    wall.AddDamage(i, -100000.0f);
                }            
            }

            var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
            shuttleGaps.ForEach(g => g.Remove());

            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine != respawnShuttle) continue;

                hull.OxygenPercentage = 100.0f;
                hull.Volume = 0.0f;
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine == respawnShuttle)
                {
                    if (Character.Controlled == c) Character.Controlled = null;
                    //if (networkMember.Character == c) networkMember.Character = null;
                    c.Enabled = false;

                    c.Kill(CauseOfDeath.Damage, true);
                }
            }

            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + respawnShuttle.Borders.Height));

            respawnShuttle.Velocity = Vector2.Zero;

            respawnShuttle.SubBody.Body.RestoreCollisionWith(Level.Loaded.ShaftBodies[0]);

        }

        public void WriteNetworkEvent(NetOutgoingMessage msg)
        {
            var server = networkMember as GameServer;

            msg.Write((byte)PacketTypes.Respawn);

            msg.WriteRangedInteger(0, Enum.GetNames(typeof(State)).Length, (int)state);

            switch (state)
            {
                case State.Transporting:
                    msg.Write(maxTransportTime);

                    var clients = GetClientsToRespawn();

                    server.AssignJobs(clients);
                    clients.ForEach(c => c.characterInfo.Job = new Job(c.assignedJob));

                    List<CharacterInfo> characterInfos = clients.Select(c => c.characterInfo).ToList();
                    if (server.Character != null && server.Character.IsDead) characterInfos.Add(server.CharacterInfo);

                    var waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, respawnShuttle);

                    ItemPrefab divingSuitPrefab = ItemPrefab.list.Find(ip => ip.Name == "Diving Suit") as ItemPrefab;
                    ItemPrefab oxyPrefab        = ItemPrefab.list.Find(ip => ip.Name == "Oxygen Tank") as ItemPrefab;

                    var cargoSp = WayPoint.WayPointList.Find(wp => wp.Submarine == respawnShuttle && wp.SpawnType == SpawnType.Cargo);

                    List<Item> spawnedItems = new List<Item>();

                    msg.Write((byte)characterInfos.Count);
                    for (int i = 0; i < characterInfos.Count; i++)
                    {
                        bool myCharacter = i >= clients.Count;

                        var character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, !myCharacter, false);

                        if (divingSuitPrefab != null && oxyPrefab != null)
                        {
                            Vector2 pos = cargoSp == null ? character.Position : cargoSp.Position;

                            var divingSuit  = new Item(divingSuitPrefab, pos, respawnShuttle);
                            var oxyTank     = new Item(oxyPrefab, pos, respawnShuttle);

                            divingSuit.Combine(oxyTank);

                            spawnedItems.Add(divingSuit);
                            spawnedItems.Add(oxyTank);

                            Item.Spawner.AddToSpawnedList(divingSuit);
                            Item.Spawner.AddToSpawnedList(oxyTank);
                        }

                        if (myCharacter)
                        {
                            msg.Write((byte)0);
                            server.Character = character;
                            Character.Controlled = character;
                        }
                        else
                        {
                            msg.Write((byte)clients[i].ID);
                            clients[i].Character = character;
                        }

                        character.GiveJobItems(waypoints[i]);

                        GameMain.GameSession.CrewManager.characters.Add(character);

                        server.WriteCharacterData(msg, character.Name, character);
                    }

                    GameMain.Server.SendItemSpawnMessage(spawnedItems);                
                    break;
                case State.Waiting:
                    msg.Write(CountdownStarted);
                    msg.Write(respawnTimer);
                    break;
                case State.Returning:
                    //CoroutineManager.StopCoroutines("forcepos");
                    //CoroutineManager.StartCoroutine(
                    //    ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f), "forcepos");
                    break;
            }
        }

        public void ReadNetworkEvent(NetIncomingMessage inc)
        {
            state = (State)inc.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);

            switch (state)
            {
                case State.Transporting:
                    maxTransportTime = inc.ReadSingle();

                    CountdownStarted = false;
                    ResetShuttle();

                    var client = networkMember as GameClient;

                    int clientCount = inc.ReadByte();
                    for (int i = 0; i < clientCount; i++)
                    {
                        byte clientId = inc.ReadByte();

                        client.ReadCharacterData(inc, clientId == client.ID);
                    }

                    CoroutineManager.StopCoroutines("forcepos");
                    CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
                    break;
                case State.Waiting:
                    CountdownStarted = inc.ReadBoolean();

                    ResetShuttle();

                    respawnTimer = inc.ReadSingle();
                    break;
                case State.Returning:

                    CountdownStarted = false;
                    break;
            }
        }
    }
}
