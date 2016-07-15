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
        const int MinCharactersToRespawn = 1;

        const float RespawnInterval = 20.0f;

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

        public float RespawnTimer
        {
            get { return respawnTimer; }
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

        private float respawnTimer, shuttleReturnTimer;

        public RespawnManager(NetworkMember server)
        {
            this.networkMember = server;

            respawnShuttle = new Submarine("Submarines/Shuttle Mark I.sub");
            respawnShuttle.Load(false);

            ResetShuttlePos();

            respawnShuttle.GodMode = true;
            
            shuttleDoors = new List<Door>();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;

                var steering = item.GetComponent<Steering>();
                if (steering != null) shuttleSteering = steering;

                var door = item.GetComponent<Door>();
                if (door != null) shuttleDoors.Add(door);
            }

            shuttleSteering.TargetPosition = ConvertUnits.ToSimUnits(Level.Loaded.StartPosition);

            respawnTimer = RespawnInterval;
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
            if (server.Character != null && server.Character.IsDead) characterToRespawnCount++;

            bool startCountdown = characterToRespawnCount >= MinCharactersToRespawn;

            if (startCountdown && !CountdownStarted) server.SendRespawnManagerMsg();

            CountdownStarted = startCountdown;

            if (!CountdownStarted) return;

            respawnTimer -= deltaTime;
            if (respawnTimer <= 0.0f)
            {
                respawnTimer = RespawnInterval;

                Respawn();
            }
        }

        private void UpdateTransporting(float deltaTime)
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            if (Character.CharacterList.Any(c => c.Submarine == respawnShuttle && !c.IsDead)) return;

            shuttleReturnTimer += deltaTime;
            if (shuttleReturnTimer > 10.0f)
            {
                state = State.Returning;

                server.SendRespawnManagerMsg();
                shuttleReturnTimer = 0.0f;
            }
        }

        private void UpdateReturning(float deltaTime)
        {
            shuttleReturnTimer += deltaTime;
            
            if (shuttleReturnTimer > 1.0f)
            {
                shuttleSteering.AutoPilot = true;
                shuttleSteering.MaintainPos = false;

                foreach (Door door in shuttleDoors)
                {
                    if (door.IsOpen) door.SetState(false, false, true);
                }

                var server = networkMember as GameServer;
                if (server == null) return;

                //shuttle has returned if the path has been traversed or the shuttle is close enough to the exit
                if (shuttleSteering.SteeringPath != null && shuttleSteering.SteeringPath.Finished
                    || (respawnShuttle.WorldPosition.Y + respawnShuttle.Borders.Y > Level.Loaded.StartPosition.Y - Level.ShaftHeight && 
                        Math.Abs(Level.Loaded.StartPosition.X - respawnShuttle.WorldPosition.X) < 1000.0f))
                {

                    CoroutineManager.StopCoroutines("forcepos");
                    CoroutineManager.StartCoroutine(
                        ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f), "forcepos");
                    
                    //string msg = "The transportation shuttle has returned to ";

                    //server.SendChatMessage(ChatMessage.Create("", msg, ChatMessageType.Server, null), server.ConnectedClients);
                    
                   
                    state = State.Waiting;
                    respawnTimer = RespawnInterval;

                    server.SendRespawnManagerMsg();
                }

                shuttleReturnTimer = 0.0f;
            }
        }

        private void Respawn()
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            state = State.Transporting;

            ResetShuttlePos();

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

        private void ResetShuttlePos()
        {
            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + respawnShuttle.Borders.Height));

            respawnShuttle.Velocity = Vector2.Zero;
        }

        public void WriteNetworkEvent(NetOutgoingMessage msg)
        {
            var server = networkMember as GameServer;

            msg.Write((byte)PacketTypes.Respawn);

            msg.WriteRangedInteger(0, Enum.GetNames(typeof(State)).Length, (int)state);

            switch (state)
            {
                case State.Transporting:
                    var clients = GetClientsToRespawn();

                    List<CharacterInfo> characterInfos = clients.Select(c => c.characterInfo).ToList();
                    if (server.Character != null && server.Character.IsDead) characterInfos.Add(server.CharacterInfo);

                    var waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, respawnShuttle);

                    msg.Write((byte)characterInfos.Count);
                    for (int i = 0; i < characterInfos.Count; i++)
                    {
                        var character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, true, false);

                        if (i < clients.Count)
                        {
                            msg.Write((byte)clients[i].ID);
                            clients[i].Character = character;
                        }
                        else
                        {
                            msg.Write((byte)0);
                            server.Character = character;
                            Character.Controlled = character;
                        }

                        character.GiveJobItems(waypoints[i]);

                        GameMain.GameSession.CrewManager.characters.Add(character);

                        server.WriteCharacterData(msg, character.Name, character);
                    }
                                        
                    break;
                case State.Waiting:
                    msg.Write(respawnTimer);
                    break;
            }
        }

        public void ReadNetworkEvent(NetIncomingMessage inc)
        {
            state = (State)inc.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);

            switch (state)
            {
                case State.Transporting:
                    CountdownStarted = false;
                    ResetShuttlePos();

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
                    CountdownStarted = true;
                    respawnTimer = inc.ReadSingle();
                    break;
                case State.Returning:
                    CountdownStarted = false;
                    break;
            }
        }
    }
}
