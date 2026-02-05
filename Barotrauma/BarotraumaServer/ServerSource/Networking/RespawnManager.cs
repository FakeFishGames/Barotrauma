using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class RespawnManager : Entity, IServerSerializable
    {
        private float shuttleEmptyTimer;

        public bool IsShuttleInsideLevel => RespawnShuttles.Any(s => s.WorldPosition.Y < Level.Loaded.Size.Y);

        private IEnumerable<Client> GetClientsToRespawn(CharacterTeamType teamId)
        {
            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            foreach (Client c in networkMember.ConnectedClients)
            {
                if (!c.InGame) { continue; }
                if (c.SpectateOnly && (GameMain.Server.ServerSettings.AllowSpectating || GameMain.Server.OwnerConnection == c.Connection)) { continue; }
                if (c.Character != null && !c.Character.IsDead) { continue; }
                if (c.TeamID != CharacterTeamType.None && c.TeamID != teamId)
                {
                    continue;
                }

                var matchingData = campaign?.GetClientCharacterData(c);
                
                //don't allow respawn if the client already has a character (they'll regain control once they're in sync)
                if (matchingData != null && matchingData.HasSpawned &&
                    Character.CharacterList.Any(c => c.Info == matchingData.CharacterInfo && !c.IsDead))
                {
                    continue;
                }
                
                // Respawning can still happen in permadeath mode (disconnected characters, reserve bench...), but never for permanently dead ones
                if (GameMain.NetworkMember?.ServerSettings is { RespawnMode: RespawnMode.Permadeath } &&
                    matchingData is not { ChosenNewBotViaShuttle: true } && // respawning as a bot that should respawn the usual way via shuttle
                    (matchingData?.CharacterInfo is { PermanentlyDead: true } || c.Character is { IsDead: true }))
                {
                    continue;
                }
                
                if (campaign != null)
                {
                    if (matchingData != null && matchingData.HasSpawned && !matchingData.ChosenNewBotViaShuttle)
                    {
                        //in the campaign mode, wait for the client to choose whether they want to spawn 
                        if (!c.WaitForNextRoundRespawn.HasValue || c.WaitForNextRoundRespawn.Value) { continue; }
                    }
                }

                yield return c;
            }
        }

        private static bool IsRespawnDecisionPendingForClient(Client c)
        {
            if (Level.Loaded == null || GameMain.GameSession.GameMode is not MultiPlayerCampaign campaign) { return false; }

            if (!c.InGame) { return false; }
            if (c.SpectateOnly && (GameMain.Server.ServerSettings.AllowSpectating || GameMain.Server.OwnerConnection == c.Connection)) { return false; }
            if (c.Character != null && !c.Character.IsDead) { return false; }

            CharacterCampaignData matchingData = campaign.GetClientCharacterData(c);
            if (matchingData != null && matchingData.HasSpawned)
            {
                if (Character.CharacterList.Any(c => 
                    c.Info == matchingData.CharacterInfo && 
                    (!c.IsDead || c.CauseOfDeath is { Type: CauseOfDeathType.Disconnected })))
                {
                    return false;
                }
                else if (!c.WaitForNextRoundRespawn.HasValue)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ClientHasChosenNewBotViaShuttle(Client c)
        {
            if (GameMain.GameSession.GameMode is MultiPlayerCampaign mpCampaign &&
                mpCampaign.GetClientCharacterData(c) is CharacterCampaignData matchingData)
            {
                return matchingData.ChosenNewBotViaShuttle;
            }
            return false;
        }

        private static List<CharacterInfo> GetBotsToRespawn(CharacterTeamType teamId)
        {
            //this works under the assumption that GetCharacterInfos only returns bots in MP
            var botInfos = GameMain.GameSession.CrewManager.GetCharacterInfos()
                .Where(botInfo => botInfo.TeamID == teamId)
                //filter out players in case a player has been given control of a bot using console commands
                .Where(botInfo => GameMain.Server.ConnectedClients.None(c => c.CharacterInfo == botInfo))
                .ToList();

            if (GameMain.Server.ServerSettings.BotSpawnMode == BotSpawnMode.Normal)
            {
                return botInfos.Where(ci => ci.Character == null || ci.Character.IsDead).ToList();
            }

            int currPlayerCount = GameMain.Server.ConnectedClients.Count(c =>
                c.InGame &&
                (!c.SpectateOnly || (!GameMain.Server.ServerSettings.AllowSpectating && GameMain.Server.OwnerConnection != c.Connection)));

            var existingBots = Character.CharacterList.FindAll(c => c.IsBot && !c.IsDead && c.TeamID == teamId);
            int requiredBots = GameMain.Server.ServerSettings.BotCount - currPlayerCount;
            requiredBots -= existingBots.Count(b => !b.IsDead);

            List<CharacterInfo> botsToRespawn = new List<CharacterInfo>();
            for (int i = 0; i < requiredBots; i++)
            {
                CharacterInfo botToRespawn = botInfos.FirstOrDefault(b => b.Character == null || b.Character.IsDead);
                if (botToRespawn == null)
                {
                    botToRespawn = new CharacterInfo(CharacterPrefab.HumanSpeciesName)
                    {
                        TeamID = teamId
                    };
                }
                else
                {
                    botInfos.Remove(botToRespawn);
                    existingBots.Remove(botToRespawn.Character);
                }
                botsToRespawn.Add(botToRespawn);
            }
            return botsToRespawn;
        }

        private string GetRespawnShuttleText(CharacterTeamType team)
        {
            if (teamSpecificStates.Count == 1)
            {
                return "respawn shuttle";
            }
            return team == CharacterTeamType.Team1 ? "respawn shuttle (team 1)" : "respawn shuttle (team 2)";
        }
        private string GetTeamNameText(CharacterTeamType team)
        {
            if (teamSpecificStates.Count == 1)
            {
                return "everyone";
            }
            return team == CharacterTeamType.Team1 ? "team 1" : "team 2";
        }


        private bool ShouldStartRespawnCountdown(TeamSpecificState teamSpecificState)
        {
            int characterToRespawnCount = GetClientsToRespawn(teamSpecificState.TeamID).Count();
            return ShouldStartRespawnCountdown(characterToRespawnCount);
        }

        private static int GetMinCharactersToRespawn()
        {
            int respawnableClientCount = GameMain.Server.ConnectedClients.Count(c => c.InGame && (!c.AFK || !GameMain.Server.ServerSettings.AllowAFK));
            return Math.Max((int)(respawnableClientCount * GameMain.Server.ServerSettings.MinRespawnRatio), 1);
        }

        private bool ShouldStartRespawnCountdown(int characterToRespawnCount)
        {
            return characterToRespawnCount >= GetMinCharactersToRespawn();
        }

        partial void UpdateWaiting(TeamSpecificState teamSpecificState)
        {
            //no respawns in the first minute of the round - otherwise it can be that bots
            //are respawned to "fill" the spots of players who are taking a long time to load in
            if (GameMain.GameSession is { RoundDuration: < 60 })
            {
                return;
            }

            var teamId = teamSpecificState.TeamID;
            var respawnShuttle = GetShuttle(teamId);
            if (respawnShuttle != null)
            {
                respawnShuttle.Velocity = Vector2.Zero;
            }

            teamSpecificState.PendingRespawnCount = GetClientsToRespawn(teamId).Count();
            if (GameMain.GameSession?.Campaign == null)
            {
                teamSpecificState.PendingRespawnCount += GetBotsToRespawn(teamId).Count;
            }
            teamSpecificState.RequiredRespawnCount = GetMinCharactersToRespawn();
            if (teamSpecificState.PendingRespawnCount != teamSpecificState.PrevPendingRespawnCount ||
                 teamSpecificState.RequiredRespawnCount != teamSpecificState.PrevRequiredRespawnCount)
            {
                teamSpecificState.PrevPendingRespawnCount = teamSpecificState.PendingRespawnCount;
                teamSpecificState.PrevRequiredRespawnCount = teamSpecificState.RequiredRespawnCount;
                GameMain.Server.CreateEntityEvent(this);
            }

            if (teamSpecificState.RespawnCountdownStarted)
            {
                if (teamSpecificState.PendingRespawnCount == 0)
                {
                    teamSpecificState.RespawnCountdownStarted = false;
                    GameMain.Server.CreateEntityEvent(this);
                }
            }
            else
            {
                bool shouldStartCountdown = ShouldStartRespawnCountdown(teamSpecificState.PendingRespawnCount);       
                if (shouldStartCountdown)
                {
                    teamSpecificState.RespawnCountdownStarted = true;
                    if (teamSpecificState.RespawnTime < DateTime.Now)
                    {
                        teamSpecificState.RespawnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, (int)(GameMain.Server.ServerSettings.RespawnInterval * 1000.0f));
                    }
                    GameMain.Server.CreateEntityEvent(this); 
                }
            }

            if (teamSpecificState.RespawnCountdownStarted && DateTime.Now > teamSpecificState.RespawnTime)
            {
                DispatchShuttle(teamSpecificState);
                teamSpecificState.RespawnCountdownStarted = false;
            }
        }

        private void DispatchShuttle(TeamSpecificState teamSpecificState)
        {
            if (RespawnShuttles.Any())
            {
                ResetShuttle(teamSpecificState);
                teamSpecificState.CurrentState = State.Transporting;
                GameMain.Server.CreateEntityEvent(this);
                SetShuttleBodyType(teamSpecificState.TeamID, FarseerPhysics.BodyType.Dynamic);
            }
            else
            {
                teamSpecificState.CurrentState = State.Waiting;
                GameServer.Log($"Respawning {GetTeamNameText(teamSpecificState.TeamID)} in the main sub.", ServerLog.MessageType.Spawning);
                GameMain.Server.CreateEntityEvent(this);
            }
            RespawnCharacters(teamSpecificState);
        }

        partial void UpdateReturningProjSpecific(TeamSpecificState teamSpecificState, float deltaTime)
        {
            //speed up despawning if there's no-one inside the shuttle
            if (teamSpecificState.DespawnTime > DateTime.Now + new TimeSpan(0, 0, seconds: 30) && CheckShuttleEmpty(deltaTime))
            {
                teamSpecificState.DespawnTime = DateTime.Now + new TimeSpan(0, 0, seconds: 30);
            }

            foreach (Door door in shuttleDoors[teamSpecificState.TeamID])
            {
                if (door.IsOpen)
                {
                    door.TrySetState(open: false, isNetworkMessage: false, sendNetworkMessage: true);
                }
            }

            var shuttleGaps = Gap.GapList.FindAll(g => RespawnShuttles.Contains(g.Submarine) && g.ConnectedWall != null);
            shuttleGaps.ForEach(g => Spawner.AddEntityToRemoveQueue(g));

            var dockingPorts = Item.ItemList.FindAll(i => RespawnShuttles.Contains(i.Submarine) && i.GetComponent<DockingPort>() != null);
            dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());

            if (!IsShuttleInsideLevel || DateTime.Now > teamSpecificState.DespawnTime)
            {
                ResetShuttle(teamSpecificState);

                teamSpecificState.CurrentState = State.Waiting;
                GameServer.Log($"The {GetRespawnShuttleText(teamSpecificState.TeamID)} has left.", ServerLog.MessageType.Spawning);
                GameMain.Server.CreateEntityEvent(this);

                teamSpecificState.RespawnCountdownStarted = false;
                teamSpecificState.ReturnCountdownStarted = false;
            }
        }

        partial void UpdateTransportingProjSpecific(TeamSpecificState teamSpecificState, float deltaTime)
        {
            if (!teamSpecificState.ReturnCountdownStarted)
            {
                //if there are no living chracters inside, transporting can be stopped immediately
                if (CheckShuttleEmpty(deltaTime))
                {
                    teamSpecificState.ReturnTime = DateTime.Now;
                    teamSpecificState.ReturnCountdownStarted = true;
                }
                else if (!ShouldStartRespawnCountdown(teamSpecificState))
                {
                    //don't start counting down until someone else needs to respawn
                    teamSpecificState.ReturnTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: (int)(maxTransportTime * 1000));
                    teamSpecificState.DespawnTime = teamSpecificState.ReturnTime + new TimeSpan(0, 0, seconds: 30);
                    return;
                }
                else
                {
                    teamSpecificState.ReturnCountdownStarted = true;
                    GameMain.Server.CreateEntityEvent(this);
                }
            }
            else if (CheckShuttleEmpty(deltaTime))
            {
                teamSpecificState.ReturnTime = DateTime.Now;
            }

            if (DateTime.Now > teamSpecificState.ReturnTime)
            {
                if (IsShuttleInsideLevel)
                {
                    GameServer.Log($"The {GetRespawnShuttleText(teamSpecificState.TeamID)} is leaving.", ServerLog.MessageType.ServerMessage);
                }
                teamSpecificState.CurrentState = State.Returning;

                GameMain.Server.CreateEntityEvent(this);

                teamSpecificState.RespawnCountdownStarted = false;
                maxTransportTime = GameMain.Server.ServerSettings.MaxTransportTime;
            }
        }

        private bool CheckShuttleEmpty(float deltaTime)
        {
            if (RespawnShuttles.All(respawnShuttle => Character.CharacterList.None(c => c.Submarine == respawnShuttle && !c.IsDead)))
            {
                shuttleEmptyTimer += deltaTime;
            }
            else
            {
                shuttleEmptyTimer = 0.0f;
            }
            return shuttleEmptyTimer > 1.0f;
        }

        private void RespawnCharacters(TeamSpecificState teamSpecificState)
        {
            var teamID = teamSpecificState.TeamID;
            int teamIndex = teamID == CharacterTeamType.Team1 ? 0 : 1;
            bool anyCharacterSpawnedInShuttle = false;
            teamSpecificState.RespawnedCharacters.Clear();

            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            bool isPvPMode = GameMain.GameSession.GameMode is PvPMode;
            int teamCount = isPvPMode ? 2 : 1;

            var clients = GetClientsToRespawn(teamID).ToList();
            foreach (Client c in clients)
            {
                // Get rid of the existing character
                if (c.Character is Character character) { character.DespawnNow(); }

                c.WaitForNextRoundRespawn = null;

                var matchingData = campaign?.GetClientCharacterData(c);
                if (matchingData != null)
                {
                    c.CharacterInfo = matchingData.CharacterInfo;
                }

                c.CharacterInfo ??= new CharacterInfo(CharacterPrefab.HumanSpeciesName, c.Name);

                //force everyone to team 1 if there's just one team
                if (teamCount == 1)
                {
                    c.TeamID = teamID;
                }
                else if (isPvPMode && c.TeamID == CharacterTeamType.None)
                {
                    GameMain.Server.AssignClientToPvpTeamMidgame(c);
                }
                c.CharacterInfo.TeamID = c.TeamID;
            }
            List<CharacterInfo> characterInfos = clients.Select(c => c.CharacterInfo).ToList();

            //bots don't respawn in the campaign
            var botsToSpawn = GetBotsToRespawn(teamID);
            if (campaign == null)
            {
                characterInfos.AddRange(botsToSpawn);
                foreach (var bot in botsToSpawn)
                {
                    // Get rid of the existing bots' corpses
                    if (bot.Character is Character character) { character.DespawnNow(); }
                }
            }

            GameMain.Server.AssignJobs(clients);
            foreach (Client c in clients)
            {
                if (campaign?.GetClientCharacterData(c) == null || c.CharacterInfo.Job == null)
                {
                    c.CharacterInfo.Job = new Job(c.AssignedJob.Prefab, isPvPMode, Rand.RandSync.Unsynced, c.AssignedJob.Variant);
                }
            }

            System.Diagnostics.Debug.Assert(characterInfos.All(c => c.TeamID == teamID), 
                "List of characters to respawn contained characters from the wrong team.");
        
            Submarine mainSub = Submarine.MainSubs[teamIndex];
            Submarine respawnSub = null;

            Submarine respawnShuttle = GetShuttle(teamID);
            Vector2? shuttlePos = null;
            if (respawnShuttle != null)
            {
                respawnSub = respawnShuttle;
                shuttlePos = FindSpawnPos(respawnShuttle, mainSub);
            }

            respawnSub ??= mainSub ?? Level.Loaded.StartOutpost;

            ItemPrefab divingSuitPrefab = null;
            if ((shuttlePos != null && Level.Loaded.GetRealWorldDepth(shuttlePos.Value.Y) > Level.DefaultRealWorldCrushDepth) ||
                (mainSub != null && Level.Loaded.GetRealWorldDepth(mainSub.WorldPosition.Y) > Level.DefaultRealWorldCrushDepth))
            {
                divingSuitPrefab = ItemPrefab.Prefabs.FirstOrDefault(it => it.Tags.Any(t => t == "respawnsuitdeep"));
            }
            divingSuitPrefab ??=
                    ItemPrefab.Prefabs.FirstOrDefault(it => it.Tags.Any(t => t == "respawnsuit")) ??
                    ItemPrefab.Find(null, "divingsuit".ToIdentifier());
            ItemPrefab oxyPrefab = ItemPrefab.Find(null, "oxygentank".ToIdentifier());
            ItemPrefab scooterPrefab = ItemPrefab.Find(null, "underwaterscooter".ToIdentifier());
            ItemPrefab batteryPrefab = ItemPrefab.Find(null, "batterycell".ToIdentifier());

            //the spawnpoints where the characters will spawn
            var selectedSpawnPoints =
                isPvPMode && Level.Loaded != null && Level.Loaded.ShouldSpawnCrewInsideOutpost() ? 
                WayPoint.SelectOutpostSpawnPoints(characterInfos, teamID) :
                WayPoint.SelectCrewSpawnPoints(characterInfos, respawnSub);

            //the spawnpoints where they would spawn if they were spawned inside the main sub
            //(in order to give them appropriate ID card tags)
            var mainSubSpawnPoints = mainSub != null ? WayPoint.SelectCrewSpawnPoints(characterInfos, mainSub) : null;
            var cargoSp = WayPoint.WayPointList.Find(wp => wp.Submarine == respawnSub && wp.SpawnType == SpawnType.Cargo);

            for (int i = 0; i < characterInfos.Count; i++)
            {
                var characterInfo = characterInfos[i];

                bool bot = botsToSpawn.Contains(characterInfo);
                characterInfo.ClearCurrentOrders();

                CharacterCampaignData characterCampaignData = null;
                bool forceSpawnInMainSub = false;
                if (!bot)
                {
                    //the client has opted to change the name of their new character
                    //when the character spawns, set the client's name to match
                    if (clients[i].PendingName == characterInfo.Name)
                    {
                        GameMain.Server?.TryChangeClientName(clients[i], clients[i].PendingName, clientRenamingSelf: true);
                        clients[i].PendingName = null;
                    }

                    characterCampaignData = campaign?.GetClientCharacterData(clients[i]);
                    if (characterCampaignData != null)
                    {
                        if (!characterCampaignData.HasSpawned)
                        {
                            forceSpawnInMainSub = true;
                        }
                        else
                        {
                            ReduceCharacterSkillsOnDeath(characterInfo);
                            characterInfo.RemoveSavedStatValuesOnDeath();
                            characterInfo.CauseOfDeath = null;
                        }
                    }
                }

                if (!forceSpawnInMainSub && respawnShuttle != null)
                {
                    anyCharacterSpawnedInShuttle = true;                        
                }

                var character = Character.Create(characterInfo, (forceSpawnInMainSub ? mainSubSpawnPoints[i] : selectedSpawnPoints[i]).WorldPosition, characterInfo.Name, isRemotePlayer: !bot, hasAi: bot);
                characterCampaignData?.ApplyWalletData(character);
                character.LoadTalents();
                if (characterInfo.LastRewardDistribution.TryUnwrap(out int salary))
                {
                    character.Wallet.SetRewardDistribution(salary);
                }

                teamSpecificState.RespawnedCharacters.Add(character);

                if (bot)
                {
                    GameServer.Log(string.Format("Respawning bot {0} as {1}.", character.Info.Name, characterInfo.Job.Name), ServerLog.MessageType.Spawning);
                }
                else
                {
                    if (GameMain.GameSession?.GameMode is MultiPlayerCampaign mpCampaign && character.Info != null)
                    {
                        character.Info.SetExperience(Math.Max(character.Info.ExperiencePoints, mpCampaign.GetSavedExperiencePoints(clients[i])));
                        mpCampaign.ClearSavedExperiencePoints(clients[i]);
                    }

                    //tell the respawning client they're no longer a traitor
                    if (GameMain.Server.TraitorManager != null && clients[i].Character != null)
                    {
                        if (GameMain.Server.TraitorManager.IsTraitor(clients[i].Character))
                        {
                            GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("TraitorRespawnMessage"), clients[i], ChatMessageType.ServerMessageBox);
                        }
                    }

                    clients[i].Character = character;
                    character.SetOwnerClient(clients[i]);
                    GameServer.Log(
                        $"Respawning {GameServer.ClientLogName(clients[i])} ({clients[i].Connection.Endpoint}) as {characterInfo.Job.Name}.", ServerLog.MessageType.Spawning);
                }

                if (respawnShuttle != null && anyCharacterSpawnedInShuttle)
                {
                    GameServer.Log($"Dispatching the {GetRespawnShuttleText(teamID)}.", ServerLog.MessageType.Spawning);
                    respawnShuttle.SetPosition(shuttlePos.Value);
                    respawnShuttle.Velocity = Vector2.Zero;
                    respawnShuttle.NeutralizeBallast();
                    respawnShuttle.EnableMaintainPosition();
                    shuttleSteering[teamID].ForEach(s => s.TargetVelocity = Vector2.Zero);

                    List<Item> newRespawnItems = new List<Item>();
                    Vector2 pos = cargoSp?.Position ?? character.Position;
                    if (divingSuitPrefab != null)
                    {
                        var divingSuit = new Item(divingSuitPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(divingSuit));
                        newRespawnItems.Add(divingSuit);

                        if (oxyPrefab != null && divingSuit.GetComponent<ItemContainer>() != null)
                        {
                            var oxyTank = new Item(oxyPrefab, pos, respawnSub);
                            Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(oxyTank));
                            divingSuit.Combine(oxyTank, user: null);
                            newRespawnItems.Add(oxyTank);
                        }
                    }

                    if (GameMain.GameSession.GameMode is not CampaignMode)
                    {
                        if (scooterPrefab != null)
                        {
                            var scooter = new Item(scooterPrefab, pos, respawnSub);
                            Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(scooter));
                            newRespawnItems.Add(scooter);
                            if (batteryPrefab != null)
                            {
                                var battery = new Item(batteryPrefab, pos, respawnSub);
                                Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(battery));
                                scooter.Combine(battery, user: null);
                                newRespawnItems.Add(battery);
                            }
                        }
                    }
                    //try to put the items in containers in the shuttle
                    foreach (var respawnItem in newRespawnItems)
                    {
                        System.Diagnostics.Debug.Assert(!respawnItem.Removed);
                        //already in a container (a battery we just placed in a scooter?) -> don't move to a cabinet
                        if (respawnItem.Container == null)
                        {
                            foreach (Item shuttleItem in respawnShuttle.GetItems(alsoFromConnectedSubs: false))
                            {
                                if (shuttleItem.NonInteractable || shuttleItem.NonPlayerTeamInteractable) { continue; }
                                var container = shuttleItem.GetComponent<ItemContainer>();
                                if (container != null && container.Inventory.TryPutItem(respawnItem, user: null))
                                {
                                    break;
                                }
                            }
                        }
                        teamSpecificState.RespawnItems.Add(respawnItem);
                    }

                    foreach (var respawnContainer in respawnContainers[teamID])
                    {
                        teamSpecificState.RespawnItems.AddRange(AutoItemPlacer.RegenerateLoot(respawnShuttle, respawnContainer));
                    }
                }

                var characterData = campaign?.GetClientCharacterData(clients[i]);
                // NOTE: This was where Reaper's tax got applied
                if (characterData != null && Level.Loaded?.Type != LevelData.LevelType.Outpost && characterData.HasSpawned)
                {
                    ReduceCharacterSkillsOnDeath(characterInfos[i], applyExtraSkillLoss: true);
                }
                WayPoint jobItemSpawnPoint = mainSubSpawnPoints != null ? mainSubSpawnPoints[i] : selectedSpawnPoints[i];
                if (characterData == null || characterData.HasSpawned)
                {
                    //give the character the items they would've gotten if they had spawned in the main sub
                    character.GiveJobItems(isPvPMode, jobItemSpawnPoint);
                    if (campaign != null)
                    {
                        characterData = campaign.SetClientCharacterData(clients[i]);
                        characterData.HasSpawned = true;
                    }
                }
                else
                {
                    if (characterData.HasItemData)
                    {
                        characterData.SpawnInventoryItems(character, character.Inventory);
                    }
                    else
                    {
                        character.GiveJobItems(isPvPMode, jobItemSpawnPoint);
                    }
                    characterData.ApplyHealthData(character);
                    character.GiveIdCardTags(jobItemSpawnPoint);
                    characterData.HasSpawned = true;
                }

                //add the ID card tags they should've gotten when spawning in the shuttle
                character.GiveIdCardTags(selectedSpawnPoints[i], createNetworkEvent: true);
            }
            
        }

        /// <summary>
        /// Reduce any skill gains the character may have made over the job's default
        /// skill levels by percentages defined in server settings. There are two
        /// reductions, a base one that always applies, and an extra loss that only
        /// applies when the player chooses to respawn ASAP rather than wait.
        /// </summary>
        public static void ReduceCharacterSkillsOnDeath(CharacterInfo characterInfo, bool applyExtraSkillLoss = false)
        {
            if (characterInfo?.Job == null) { return; }

            float resistanceMultiplier;
            float skillLossPercentage;
            if (applyExtraSkillLoss)
            {
                DebugConsole.Log($"Calculating extra skill loss on respawn for {characterInfo.Name}:");
                resistanceMultiplier = characterInfo.LastResistanceMultiplierSkillLossRespawn;
                skillLossPercentage = SkillLossPercentageOnImmediateRespawn;
            }
            else
            {
                DebugConsole.Log($"Calculating base skill loss on death for {characterInfo.Name}:");
                resistanceMultiplier = characterInfo.LastResistanceMultiplierSkillLossDeath;
                skillLossPercentage = SkillLossPercentageOnDeath;
            }
            skillLossPercentage *= resistanceMultiplier;

            foreach (Skill skill in characterInfo.Job.GetSkills())
            {
                skill.Level = GetReducedSkill(characterInfo, skill, skillLossPercentage);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteByte((byte)c.TeamID);
            foreach (var teamSpecificState in teamSpecificStates.Values)
            {
                msg.WriteByte((byte)teamSpecificState.TeamID);
                msg.WriteRangedInteger((int)teamSpecificState.CurrentState, 0, Enum.GetNames(typeof(State)).Length);

                switch (teamSpecificState.CurrentState)
                {
                    case State.Transporting:
                        msg.WriteBoolean(teamSpecificState.ReturnCountdownStarted);
                        msg.WriteSingle(GameMain.Server.ServerSettings.MaxTransportTime);
                        msg.WriteSingle((float)(teamSpecificState.ReturnTime - DateTime.Now).TotalSeconds);
                        break;
                    case State.Waiting:
                        msg.WriteUInt16((ushort)teamSpecificState.PendingRespawnCount);
                        msg.WriteUInt16((ushort)teamSpecificState.RequiredRespawnCount);
                        msg.WriteBoolean(IsRespawnDecisionPendingForClient(c));
                        msg.WriteBoolean(ClientHasChosenNewBotViaShuttle(c));
                        msg.WriteBoolean(teamSpecificState.RespawnCountdownStarted);
                        msg.WriteSingle((float)(teamSpecificState.RespawnTime - DateTime.Now).TotalSeconds);
                        break;
                    case State.Returning:
                        break;
                }
            }

            msg.WritePadBits();
        }
    }
}
