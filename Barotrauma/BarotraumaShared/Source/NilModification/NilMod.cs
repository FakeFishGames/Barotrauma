using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Networking;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.IO;

namespace Barotrauma
{
    class ModifiedCharacterStat
    {
        public Character character;
        public Boolean UpdateHealth;
        public Boolean UpdateBleed;
        public Boolean UpdateOxygen;
        public float newhealth;
        public float newbleed;
        public float newoxygen;
    }

    class ConvertingHusk
    {
        public Character character;
        //public int Updatestildeletion;
    }

    class DisconnectedCharacter
    {
        public string clientname;
        public string IPAddress;
        public Character character;
        public float TimeUntilKill;
        public float DisconnectStun;
        public float ClientSetCooldown;
    }

    class KickedClient
    {
        public string clientname;
        public string IPAddress;
        public float ExpireTimer;
        public float RejoinTimer;
        public string KickReason;
    }

    class NilMod
    {
        public List<ConvertingHusk> convertinghusklist = new List<ConvertingHusk>();

        const string SettingsSavePath = "Data/NilModSettings.xml";
        public Boolean SetToDefault = false;
        public Boolean NilModSetupDefaultServer = false;
        public Boolean Skippedtoserver = false;
        public string RoundSaveName = "";

        //Server Packet Handling Storage
        public List<Character> characterstoupdate;
        public List<Submarine> subtoupdate;
        public List<Item> itemtoupdate;
        public int PacketNumber;

        public Boolean UseDesyncPrevention;
        public List<Item> DesyncPreventionItemList;
        public float DesyncPreventionItemPassTimer;
        public float DesyncPreventionItemPassTimerleft;
        public int DesyncPreventionPassItemCount;
        public List<Character> DesyncPreventionPlayerStatusList;
        public float DesyncPreventionPlayerStatusTimer;
        public float DesyncPreventionPlayerStatusTimerleft;
        public int DesyncPreventionPassPlayerStatusCount;
        public List<Hull> DesyncPreventionHullList;
        public float DesyncPreventionHullStatusTimer;
        public float DesyncPreventionHullStatusTimerleft;
        public int DesyncPreventionPassHullCount;


        public List<Character> FrozenCharacters;
        public List<DisconnectedCharacter> DisconnectedCharacters;
        public List<KickedClient> KickedClients;
        public List<ModifiedCharacterStat> ModifiedCharacterValues;

        public float CharFlashColourTime;
        public const float CharFlashColourRate = 2f;
        public float BanReloadTimer;

        //Left and right click variables
        public Boolean ActiveClickCommand;
        public string ClickCommandType = "";
        public float ClickCooldown = 0f;
        public float ClickFindSelectionDistance = 100f;
        public const float ClickCooldownPeriod = 0.2f;
        public string ClickArgOne = "";
        public string ClickArgTwo = "";
        public string ClickArgThree = "";
        public string[] ClickArgs;
        public Character RelocateTarget;

        //Classes for the help system and event Chatter system
        public static NilModPermissions NilModPermissions;
        public static NilModHelpCommands NilModHelpCommands;
        public static NilModEventChatter NilModEventChatter;
        public static PlayerLog NilModPlayerLog;
        public static VPNBanlist NilModVPNBanlist;

        //Traitor Info
        public String Traitor;
        public String TraitorTarget;

        //Character Lists - Could be better used to keep track of things at a later date
        //public static List<Character> NetPlayerCharacterList = new List<Character>();
        //public static List<Character> InactivePlayerCharacterList = new List<Character>();
        //public static List<Character> HumanAIList = new List<Character>();
        //public static List<Character> CreatureList = new List<Character>();

        //Respawn Manager
        public float RemainingRespawnShuttles;  //Unimplemented Feature
        public float RemainingRespawnCharacters;  //Unimplemented Feature

        //DebugConsole
        public Boolean DisableCrushDamage;
        public float EditWaterAmount;


        //NilModSettings
        public Boolean BypassMD5;
        public string ServerMD5A;
        public string ServerMD5B;

        public Boolean DebugConsoleTimeStamp;

        public int MaxLogMessages;
        public Boolean ClearLogRoundStart;
        public Boolean LogAppendCurrentRound;
        public int LogAppendLineSaveRate;

        public float ChatboxWidth;
        public float ChatboxHeight;
        public int ChatboxMaxMessages;

        public Boolean ShowRoomInfo;
        public Boolean UseUpdatedCharHUD;
        public Boolean UseCreatureZoomBoost;
        public float CreatureZoomMultiplier;

        public Boolean StartToServer;
        public Boolean ServerModSetupDefaultServer;  //Unimplemented Feature

        public Boolean EnableEventChatterSystem;
        public Boolean EnableHelpSystem;
        public Boolean EnableAdminSystem;
        public Boolean EnablePlayerLogSystem;
        public Boolean EnableVPNBanlist;

        public string SubmarineVoters;
        public Boolean SubVotingConsoleLog;
        public Boolean SubVotingServerLog;
        public Boolean SubVotingAnnounce;

        public float BanListReloadTimer;
        public Boolean BansOverrideBannedInfo;
        public Boolean BansInfoAddBanName;
        public Boolean BansInfoAddBanDuration;
        public Boolean BansInfoUseRemainingTime;
        public Boolean BansInfoAddCustomString;
        public string BansInfoCustomtext;
        public Boolean BansInfoAddBanReason;
        public Boolean VPNBanKicksPlayer;
        public string VPNKickMainText;
        public Boolean VPNKickUseCustomString;
        public string VPNKickCustomString;

        public float VoteKickStateNameTimer;
        public float VoteKickDenyRejoinTimer;
        public float AdminKickStateNameTimer;
        public float AdminKickDenyRejoinTimer;
        public float KickStateNameTimerIncreaseOnRejoin;
        public float KickMaxStateNameTimer;
        public Boolean ClearKickStateNameOnRejoin;
		public Boolean ClearKicksOnRoundStart;


        //NilModDebug
        public Boolean DebugReportSettingsOnLoad;
        public Boolean DebugLag;
        public float DebugLagSimulatedPacketLoss;
        public float DebugLagSimulatedRandomLatency;
        public float DebugLagSimulatedDuplicatesChance;
        public float DebugLagSimulatedMinimumLatency;
        public float DebugLagConnectionTimeout;
        public Boolean ShowPacketMTUErrors;
        public Boolean ShowOpenALErrors;
        public Boolean ShowPathfindingErrors;
        public Boolean ShowMasterServerSuccess;
        public List<String> ParticleWhitelist;
        public int MaxParticles;
        public int ParticleSpawnPercent;
        public float ParticleLifeMultiplier;
        public Boolean DisableParticles = false;
        public Boolean UseCharStatOptimisation;

        //Server Settings

        public string ServerName;
        public int ServerPort;
        public int MaxPlayers;
        public Boolean UseServerPassword;
        public string ServerPassword;
        public string AdminAuth;
        public Boolean PublicServer;
        public Boolean UPNPForwarding;
        public Boolean AutoRestart;
        public string DefaultGamemode;
        public string DefaultMissionType;
        public string DefaultRespawnShuttle;
        public string DefaultSubmarine;
        public string DefaultLevelSeed;
        public Boolean SetDefaultsAlways;
        public Boolean UseAlternativeNetworking;
        public float CharacterDisabledistance;
        public float ItemPosUpdateDistance;
        public float DesyncTimerMultiplier;
        public Boolean LogAIDamage;
        public Boolean UseStartWindowPosition;
        public int StartXPos;
        public int StartYPos;
        public Boolean CrashRestart;
        public Boolean DisableParticlesOnStart;
        public Boolean DisableLightsOnStart;
        public Boolean DisableLOSOnStart;
        public Boolean AllowReconnect;
        public float ReconnectAddStun;
        public float ReconnectTimeAllowed;

        //Respawn Shuttle Stuff
        public Boolean LimitCharacterRespawns;
        public Boolean LimitShuttleRespawns;
        public int MaxRespawnCharacters;
        public int MaxRespawnShuttles;
        public float BaseRespawnCharacters;
        public float BaseRespawnShuttles;
        public float RespawnCharactersPerPlayer;
        public float RespawnShuttlesPerPlayer;
        public Boolean AlwaysRespawnNewConnections;
        public Boolean RespawnNewConnectionsToSub;
        public Boolean RespawnOnMainSub;
        public Boolean RespawnWearingSuitGear;
        public int RespawnLeavingAutoPilotMode;
        public Boolean RespawnShuttleLeavingCloseDoors;
        public Boolean RespawnShuttleLeavingUndock;
        public float RespawnShuttleLeaveAtTime;
        //Add drop item on death
        //Add Delete Character on death

        //All Character Settings
        public float PlayerOxygenUsageAmount;
        public float PlayerOxygenGainSpeed;
        public Boolean UseProgressiveImplodeDeath;
        public float ImplodeHealthLoss;
        public float ImplodeBleedGain;
        public float ImplodeOxygenLoss;
        public Boolean PreventImplodeHealing;
        public Boolean PreventImplodeClotting;
        public Boolean PreventImplodeOxygen;
        public Boolean CharacterImplodeDeathAtMinHealth;
        public float HuskHealingMultiplierinfected;
        public float HuskHealingMultiplierincurable;
        public float PlayerHuskInfectedDrain;
        public float PlayerHuskIncurableDrain;
        public Boolean AverageDecayIfBothNegative;
        public float HealthUnconciousDecayHealth;
        public float HealthUnconciousDecayBleed;
        public float HealthUnconciousDecayOxygen;
        public float OxygenUnconciousDecayHealth;
        public float OxygenUnconciousDecayBleed;
        public float OxygenUnconciousDecayOxygen;
        public float MinHealthBleedCap;
        public float CreatureBleedMultiplier;
        //This needs to be changed into a percent instead : >
        public Boolean ArmourBleedBypassNoDamage;
        public Boolean ArmourSoakFireDamage;
        public float ArmourAbsorptionHealth;
        public float ArmourDirectReductionHealth;
        public float ArmourResistanceMultiplierHealth;
        public float ArmourResistancePowerHealth;
        public float ArmourMinimumHealthPercent;
        public float ArmourAbsorptionBleed;
        public float ArmourDirectReductionBleed;
        public float ArmourResistanceMultiplierBleed;
        public float ArmourResistancePowerBleed;
        public float ArmourMinimumBleedPercent;

        //Player Settings
        public Boolean PlayerCanTraumaDeath;
        public Boolean PlayerCanImplodeDeath;
        public Boolean PlayerCanSuffocateDeath;
        public float PlayerHealthMultiplier;
        public float PlayerHuskHealthMultiplier;
        public Boolean PlayerHuskAiOnDeath;
        public float PlayerHealthRegen;
        public float PlayerHealthRegenMin;
        public float PlayerHealthRegenMax;
        public Boolean PlayerCPRStopsOxygenDecay;
        public Boolean PlayerCPRStopsBleedDecay;
        public Boolean PlayerCPRStopsHealthDecay;
        public Boolean PlayerCPROnlyWhileUnconcious;
        public float PlayerCPRHealthBaseValue;
        public float PlayerCPRHealthSkillMultiplier;
        public int PlayerCPRHealthSkillNeeded;
        public float PlayerCPRStunBaseValue;
        public float PlayerCPRStunSkillMultiplier;
        public int PlayerCPRStunSkillNeeded;
        public float PlayerCPRClotBaseValue;
        public float PlayerCPRClotSkillMultiplier;
        public int PlayerCPRClotSkillNeeded;
        public float PlayerCPROxygenBaseValue;
        public float PlayerCPROxygenSkillMultiplier;
        public float PlayerUnconciousTimer;

        //Creature Related
        public float CreatureHealthMultiplier;
        public float CreatureHealthRegen;
        public float CreatureHealthRegenMin;
        public float CreatureHealthRegenMax;
        public Boolean CreatureEatDyingPlayers;
        //public Boolean CreatureAttackDyingPlayers;
        //public float PlayerDyingthreshold;
        //public float CreatureEatingSpeedMultiplier;
        //public float CreatureGainEatingFlat;
        //public float CreatureGainEatingPercent;
        public Boolean CreatureRespawnMonsterEvents;
        public Boolean CreatureLimitRespawns;
        public int CreatureMaxRespawns;
        //Add fish auto-deletion on death
        //Add fish deletion timer on death (Allows them to be eaten still?)

        //Submarine
        public float HullOxygenDistributionSpeed;
        public float HullOxygenDetoriationSpeed;
        public float HullOxygenConsumptionSpeed;
        public float HullUnbreathablePercent;
        public Boolean SyncFireSizeChange;
        public float FireSyncFrequency;
        public float FireSizeChangeToSync;
        public float FireProofDamagePercentReduction;
        public float FireProofRangePercentReduction;
        public float FireCharDamageMultiplier;
        public float FireCharRangeMultiplier;
        public float FireItemRangeMultiplier;
        public Boolean FireUseRangedDamage;
        public float FireRangedDamageStrength;
        public float FireRangedDamageMinMultiplier;
        public float FireRangedDamageMaxMultiplier;
        public float FireOxygenConsumption;
        public float FireGrowthSpeed;
        public float FireShrinkSpeed;
        public float FireWaterExtinguishMultiplier;
        public float FireToolExtinguishMultiplier;
        public Boolean EnginesRegenerateCondition;
        public float EnginesRegenAmount;
        public Boolean ElectricalRegenerateCondition;
        public float ElectricalRegenAmount;
        public float ElectricalOverloadDamage;
        public float ElectricalOverloadMinPower;
        public float ElectricalOverloadVoltRangeMin;
        public float ElectricalOverloadVoltRangeMax;
        public float ElectricalOverloadFiresChance;
        public float ElectricalFailMaxVoltage;
        public float ElectricalFailStunTime;
        public Boolean CanDamageSubBody;
        public Boolean CanRewireMainSubs;
        //70000 = top of the map
        //-30000 = Default Crush Depth
        public float CrushDamageDepth;
        public float PlayerCrushDepthInHull;
        public float PlayerCrushDepthOutsideHull;
        public Boolean UseProgressiveCrush;
        public Boolean PCrushUseWallRemainingHealthCheck;
        public float PCrushDepthHealthResistMultiplier;
        public float PCrushDepthBaseHealthResist;
        public float PCrushDamageDepthMultiplier;
        public float PCrushBaseDamage;
        public float PCrushWallHealthDamagePercent;
        public float PCrushWallBaseDamageChance;
        public float PCrushWallDamageChanceIncrease;
        public float PCrushWallMaxDamageChance;
        public float PCrushInterval;

        //Items

        //Host Character Specific
        public Boolean HostBypassSkills;
        public string PlayYourselfName;

        //Real time updates for NilMod
        public void Update(float deltaTime)
        {
            if (GameMain.Server != null)
            {
                if (BanListReloadTimer > 0f)
                {
                    BanReloadTimer += deltaTime;
                    if (BanReloadTimer >= BanListReloadTimer)
                    {
                        BanReloadTimer = 0f;
                        if (GameMain.Server != null)
                        {
                            if (GameMain.Server.BanList != null)
                            {
                                GameMain.Server.BanList.load();
                                GameMain.Server.BanList.Save();
                            }
                        }
                    }
                }
                if (GameMain.Server.GameStarted)
                {
                    if(UseDesyncPrevention)
                    {
                        //Item position Anti Desync (Where items are on the server)
                        if(DesyncPreventionItemPassTimerleft <= 0f)
                        {
                            if (DesyncPreventionItemPassTimer > 0f && DesyncPreventionPassItemCount > 0)
                            {
                                DesyncPreventionItemPassTimerleft = DesyncPreventionItemPassTimer;

                                if (DesyncPreventionItemList.Count == 0)
                                {
                                    for (int i = 0; i < Item.ItemList.Count; i++)
                                    {
                                        if (Item.ItemList[i].body != null)
                                        {
                                            DesyncPreventionItemList.Add(Item.ItemList[i]);
                                        }
                                    }
                                }

                                for (int i = Math.Min(DesyncPreventionItemList.Count, DesyncPreventionPassItemCount) - 1; i >= 0; i--)
                                {
                                    if (DesyncPreventionItemList[i].body != null)
                                        DesyncPreventionItemList[i].NeedsPositionUpdate = true;

                                    DesyncPreventionItemList.RemoveAt(i);
                                }
                            }
                        }
                        else
                        {
                            DesyncPreventionItemPassTimerleft -= deltaTime;
                        }


                        //Character Status desync prevention (Health/Oxygen/Bleeding/Unconcious and stun etc)
                        if(DesyncPreventionPlayerStatusTimerleft <= 0f)
                        {
                            if (DesyncPreventionPlayerStatusTimer > 0f && DesyncPreventionPassPlayerStatusCount > 0)
                            {
                                DesyncPreventionPlayerStatusTimerleft = DesyncPreventionPlayerStatusTimer;

                                if (DesyncPreventionPlayerStatusList.Count == 0)
                                {
                                    //Sync netevents for host character too for other clients
                                    if (Character.Controlled != null) DesyncPreventionPlayerStatusList.Add(Character.Controlled);

                                    for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
                                    {
                                        if (GameMain.Server.ConnectedClients[i].Character != null && !GameMain.Server.ConnectedClients[i].Character.IsDead && GameMain.Server.ConnectedClients[i].inGame)
                                        {
                                            DesyncPreventionPlayerStatusList.Add(GameMain.Server.ConnectedClients[i].Character);
                                        }
                                    }
                                }

                                for (int i = Math.Min(DesyncPreventionPlayerStatusList.Count, DesyncPreventionPassPlayerStatusCount) - 1; i >= 0; i--)
                                {
                                    if (!DesyncPreventionPlayerStatusList[i].IsDead)
                                    {
                                        GameMain.Server.CreateEntityEvent(DesyncPreventionPlayerStatusList[i], new object[] { Networking.NetEntityEvent.Type.Status });
                                        DesyncPreventionPlayerStatusList[i].lastSentHealth = DesyncPreventionPlayerStatusList[i].Health;
                                        DesyncPreventionPlayerStatusList[i].lastSentOxygen = DesyncPreventionPlayerStatusList[i].Oxygen;
                                    }

                                    DesyncPreventionPlayerStatusList.RemoveAt(i);
                                }
                            }
                        }
                        else
                        {
                            DesyncPreventionPlayerStatusTimerleft -= deltaTime;
                        }



                        //Submarine and shuttle Hull Desync Prevention (Air/Fires etc)
                        if(DesyncPreventionHullStatusTimerleft <= 0f)
                        {
                            if (DesyncPreventionHullStatusTimer > 0f && DesyncPreventionPassHullCount > 0)
                            {
                                DesyncPreventionHullStatusTimerleft = DesyncPreventionHullStatusTimer;

                                if (DesyncPreventionHullList.Count == 0)
                                {
                                    for (int i = 0; i < Hull.hullList.Count; i++)
                                    {
                                        DesyncPreventionHullList.Add(Hull.hullList[i]);
                                    }
                                }

                                for (int i = Math.Min(DesyncPreventionHullList.Count, DesyncPreventionPassHullCount) - 1; i >= 0; i--)
                                {
                                    GameMain.Server.CreateEntityEvent(DesyncPreventionHullList[i]);

                                    DesyncPreventionHullList.RemoveAt(i);
                                }
                            }
                        }
                        else
                        {
                            DesyncPreventionHullStatusTimerleft -= deltaTime;
                        }
                    }

                    //Code support for allowing client reconnect
                    if (DisconnectedCharacters.Count >= 1)
                    {
                        for (int i = DisconnectedCharacters.Count - 1; i >= 0; i--)
                        {
                            if (GameMain.NilMod.AllowReconnect)
                            {
                                DisconnectedCharacters[i].TimeUntilKill -= deltaTime;

                                if (DisconnectedCharacters[i].character != null && !DisconnectedCharacters[i].character.IsDead && !DisconnectedCharacters[i].character.Removed)
                                {
                                    DisconnectedCharacters[i].character.Stun = 60f;
                                }

                                Client clientmatch = GameMain.Server?.ConnectedClients?.Find(c => c.name == DisconnectedCharacters[i].clientname && c.Connection.RemoteEndPoint.Address.ToString() == DisconnectedCharacters[i].IPAddress);

                                if (clientmatch != null && DisconnectedCharacters[i].ClientSetCooldown <= 0f && clientmatch.inGame)
                                {
                                    if (DisconnectedCharacters[i].character != null && !DisconnectedCharacters[i].character.Removed)
                                    {
                                        if (DisconnectedCharacters[i].character.IsDead)
                                        {
                                            var chatMsg = ChatMessage.Create(
                                                                "",
                                                                ("You have been reconnected to your character - However while you were gone your character has been killed."),
                                                                (ChatMessageType)ChatMessageType.Server,
                                                                null);

                                            GameMain.Server.SendChatMessage(chatMsg, clientmatch);
                                            DisconnectedCharacters[i].TimeUntilKill = 0f;
                                        }
                                        GameMain.Server.SetClientCharacter(clientmatch, DisconnectedCharacters[i].character);
                                        DisconnectedCharacters[i].ClientSetCooldown = 0.1f;
                                        DisconnectedCharacters[i].character.SetStun(0f, true, true);
                                    }
                                    else
                                    {
                                        DisconnectedCharacters[i].TimeUntilKill = 0f;
                                    }
                                }
                                else if (clientmatch != null)
                                {
                                    DisconnectedCharacters[i].ClientSetCooldown -= deltaTime;
                                }
                            }
                            else
                            {
                                DisconnectedCharacters[i].TimeUntilKill = 0f;
                            }

                            if (DisconnectedCharacters[i].TimeUntilKill <= 0f)
                            {
                                if (DisconnectedCharacters[i].character != null && !DisconnectedCharacters[i].character.Removed && !DisconnectedCharacters[i].character.IsDead)
                                {
                                    DisconnectedCharacters[i].character.Kill(CauseOfDeath.Disconnected, true);
                                }
                                DisconnectedCharacters.RemoveAt(i);
                            }
                        }
                    }
                }

                //Handle kicked clients timers
                if (KickedClients.Count >= 1)
                {
                    for (int i = KickedClients.Count - 1; i >= 0; i--)
                    {
                        if(KickedClients[i].RejoinTimer > 0f)
                        {
                            KickedClients[i].RejoinTimer -= deltaTime;
                            if (KickedClients[i].RejoinTimer < 0f) KickedClients[i].RejoinTimer = 0f;
                        }
                        else
                        {
                            KickedClients[i].ExpireTimer -= deltaTime;
                            if (KickedClients[i].ExpireTimer < 0f) KickedClients[i].ExpireTimer = 0f;
                        }

                        if (KickedClients[i].ExpireTimer == 0f && KickedClients[i].RejoinTimer == 0f)
                        {
                            KickedClients.RemoveAt(i);
                        }
                    }
                }
            }

            //Cycle the outline flash colours
            CharFlashColourTime += deltaTime;
            if (CharFlashColourTime >= CharFlashColourRate) CharFlashColourTime = 0f;

            //Countdown then remove converted husks
            /*
            if(convertinghusklist.Count() > 0)
            {
                for(int i = convertinghusklist.Count() - 1; i > 0 ; i--)
                {
                    convertinghusklist[i].Updatestildeletion -= 1;
                    if(convertinghusklist[i].Updatestildeletion <= 0)
                    {
                        Entity.Spawner.AddToRemoveQueue(convertinghusklist[i].character);
                        convertinghusklist.RemoveAt(i);
                    }
                }
            }
            */
        }

        public void ReportSettings()
        {
            GameMain.Server.ServerLog.WriteLine("Loading Nilmod Settings:", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("NilModSettings:", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Nilmod Server general settings
            GameMain.Server.ServerLog.WriteLine("BypassMD5 = " + BypassMD5.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ServerMD5 A = " + ServerMD5A.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ServerMD5 B = " + ServerMD5B.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugConsoleTimeStamp = " + DebugConsoleTimeStamp.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxLogMessages = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ClearLogRoundStart = " + (ClearLogRoundStart ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogAppendCurrentRound = " + (LogAppendCurrentRound ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogAppendLineSaveRate = " + LogAppendLineSaveRate.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ClearLogRoundStart = " + (ClearLogRoundStart ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxHeight = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxWidth = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxMaxMessages = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("StartToServer = " + StartToServer.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("EnableEventChatterSystem = " + (EnableEventChatterSystem ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("EnableHelpSystem = " + (EnableHelpSystem ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            //GameMain.Server.ServerLog.WriteLine("EnableAdminSystem = " + (EnableAdminSystem ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("EnablePlayerLogSystem = " + (EnablePlayerLogSystem ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateNames = " + (NilModPlayerLog.PlayerLogStateNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateFirstJoinedNames = " + (NilModPlayerLog.PlayerLogStateFirstJoinedNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerLogStateLastJoinedNames = " + (NilModPlayerLog.PlayerLogStateLastJoinedNames ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("EnableVPNBanlist = " + (EnableVPNBanlist ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("SubVotingConsoleLog = " + (SubVotingConsoleLog ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("SubVotingServerLog = " + (SubVotingServerLog ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("SubVotingAnnounce = " + (SubVotingAnnounce ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogAIDamage = " + (LogAIDamage ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Server autostart Related Settings
            GameMain.Server.ServerLog.WriteLine("ServerName = " + ServerName.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ServerPort = " + ServerPort.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxPlayers = " + MaxPlayers.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("UseServerPassword = " + (UseServerPassword ? "Enabled" : "Disabled") + "With Password: " + ServerPassword, ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("AdminAuth = " + AdminAuth.Length.ToString() + " Characters long", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PublicServer = " + (PublicServer ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("UPNPForwarding = " + (UPNPForwarding ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("AutoRestart = " + (AutoRestart ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DefaultGamemode = " + DefaultGamemode.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DefaultRespawnShuttle = " + DefaultRespawnShuttle.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DefaultSubmarine = " + DefaultSubmarine.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DefaultLevelSeed = " + DefaultLevelSeed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("SetDefaultsAlways = " + (SetDefaultsAlways ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("UseAlternativeNetworking = " + (UseAlternativeNetworking ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CharacterDisabledistance = " + CharacterDisabledistance.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ItemPosUpdateDistance = " + ItemPosUpdateDistance.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DesyncTimerMultiplier = " + DesyncTimerMultiplier.ToString(), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //NilMod Debugger
            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("DebugReportSettingsOnLoad = " + DebugReportSettingsOnLoad.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ShowPacketMTUErrors = " + (ShowPacketMTUErrors ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ShowOpenALErrors = " + (ShowOpenALErrors ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ShowPathfindingErrors = " + (ShowPathfindingErrors ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ShowMasterServerSuccess = " + (ShowMasterServerSuccess ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLag = " + (DebugLag ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLagSimulatedPacketLoss = " + DebugLagSimulatedPacketLoss.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLagSimulatedRandomLatency = " + DebugLagSimulatedRandomLatency.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLagSimulatedDuplicatesChance = " + DebugLagSimulatedDuplicatesChance.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLagSimulatedMinimumLatency = " + DebugLagSimulatedMinimumLatency.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugLagConnectionTimeout = " + DebugLagConnectionTimeout.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxParticles = " + MaxParticles.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ParticleSpawnPercent = " + ParticleSpawnPercent.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ParticleLifeMultiplier = " + ParticleLifeMultiplier.ToString(), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //shuttle related settings
            GameMain.Server.ServerLog.WriteLine("MaxRespawnCharacters = " + MaxRespawnCharacters.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxRespawnShuttles = " + MaxRespawnShuttles.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("RespawnOnMainSub = " + (RespawnOnMainSub ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("RespawnWearingSuitGear = " + (RespawnOnMainSub ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);

            switch (RespawnLeavingAutoPilotMode)
            {
                case 0:
                    GameMain.Server.ServerLog.WriteLine("RespawnLeavingAutoPilotMode = Autopilot Home", ServerLog.MessageType.NilMod);
                    break;
                case 1:
                    GameMain.Server.ServerLog.WriteLine("RespawnLeavingAutoPilotMode = Maintain Position", ServerLog.MessageType.NilMod);
                    break;
                case 2:
                    GameMain.Server.ServerLog.WriteLine("RespawnLeavingAutoPilotMode = No Control Loss", ServerLog.MessageType.NilMod);
                    break;
            }
            GameMain.Server.ServerLog.WriteLine("RespawnShuttleLeavingCloseDoors = " + (RespawnShuttleLeavingCloseDoors ? "Enabled - Doors close themselves on timer expiry." : "Disabled - Doors keep their current state on expiry."), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("RespawnShuttleLeavingUndock = " + (RespawnShuttleLeavingUndock ? "Enabled - Shuttle undocks itself on timer expiry." : "Disabled - Shuttle stays docked on timer expiry."), ServerLog.MessageType.NilMod);

            if (RespawnShuttleLeaveAtTime < 0f)
            {
                GameMain.Server.ServerLog.WriteLine("RespawnShuttleLeaveAtTime = Inverse of Transport Duration (DEFAULT)", ServerLog.MessageType.NilMod);
            }
            else if (RespawnShuttleLeaveAtTime == 0f)
            {
                GameMain.Server.ServerLog.WriteLine("RespawnShuttleLeaveAtTime = Instantly leaves", ServerLog.MessageType.NilMod);
            }
            else
            {
                GameMain.Server.ServerLog.WriteLine("RespawnShuttleLeaveAtTime = " + ToolBox.SecondsToReadableTime(RespawnShuttleLeaveAtTime), ServerLog.MessageType.NilMod);
            }

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Submarine Related Settings
            GameMain.Server.ServerLog.WriteLine("HullOxygenDistributionSpeed = " + HullOxygenDistributionSpeed.ToString() + " Volume of oxygen moved / second between hulls.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HullOxygenDetoriationSpeed = " + HullOxygenDetoriationSpeed.ToString() + " Volume of oxygen removed / second inside a hull (Decay)", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HullOxygenConsumptionSpeed = " + HullOxygenConsumptionSpeed.ToString() + " Volume of oxygen consumed / Player inside a hull.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HullUnbreathablePercent = " + HullUnbreathablePercent.ToString() + "% before a room is rendered unbreathable.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CanDamageSubBody = " + (CanDamageSubBody ? "Enabled - Submarine hulls may take damage via any means." : "Disabled - Submarine hulls cannot be damaged."), ServerLog.MessageType.NilMod);
            //GameMain.Server.ServerLog.WriteLine("CanRewireMainSubs = " + (CanRewireMainSubs ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CrushDamageDepth = " + CrushDamageDepth.ToString() + " worldspace Y coordinate for crush to be calculated from for subs and suits.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("UseProgressiveCrush = " + (UseProgressiveCrush ? "Enabled - Use PCrush settings to distribute crush damage to all walls." : "Disabled - Use vanilla submarine crush mechanics."), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushUseWallRemainingHealthCheck = " + (PCrushUseWallRemainingHealthCheck ? "Enabled - Wall resistance health is based off damaged sections." : "Disabled - Wall resistance health is based off max section health."), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushDepthHealthResistMultiplier = " + PCrushDepthHealthResistMultiplier.ToString() + " of a walls Health counts to resisting depth beyond max.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushDepthBaseHealthResist = " + PCrushDepthBaseHealthResist.ToString() + " health points wroth added onto all walls depth checks for damage.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushDamageDepthMultiplier = " + PCrushDamageDepthMultiplier.ToString() + " Damage dealt for depth increase.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushBaseDamage = " + PCrushBaseDamage.ToString() + " Base Damage to section.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushWallHealthDamagePercent = " + PCrushWallHealthDamagePercent.ToString() + "% of wall max health damaged.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushWallBaseDamageChance = " + PCrushWallBaseDamageChance.ToString() + "% Base chance to damage a section.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushWallDamageChanceIncrease = " + PCrushWallDamageChanceIncrease.ToString() + "% Chance increase per approx. 100 meters.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PCrushWallMaxDamageChance = " + HullUnbreathablePercent.ToString() + "% Max chance of base+increase to damage a section.", ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HullUnbreathablePercent = " + HullUnbreathablePercent.ToString(), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //All Character Related Settings
            GameMain.Server.ServerLog.WriteLine("PlayerOxygenUsageAmount = " + PlayerOxygenUsageAmount.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerOxygenGainSpeed = " + PlayerOxygenGainSpeed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("UseProgressiveImplodeDeath = " + (UseProgressiveImplodeDeath ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ImplodeHealthLoss = " + ImplodeHealthLoss.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ImplodeBleedGain = " + ImplodeBleedGain.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ImplodeOxygenLoss = " + ImplodeOxygenLoss.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PreventImplodeHealing = " + (PreventImplodeHealing ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PreventImplodeClotting = " + (PreventImplodeClotting ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PreventImplodeOxygen = " + (PreventImplodeOxygen ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CharacterImplodeDeathAtMinHealth = " + (CharacterImplodeDeathAtMinHealth ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HuskHealingMultiplierinfected = " + HuskHealingMultiplierinfected.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HuskHealingMultiplierincurable = " + HuskHealingMultiplierincurable.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHuskInfectedDrain = " + PlayerHuskInfectedDrain.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHuskIncurableDrain = " + PlayerHuskIncurableDrain.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HealthUnconciousDecayHealth = " + HealthUnconciousDecayHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HealthUnconciousDecayBleed = " + HealthUnconciousDecayBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HealthUnconciousDecayOxygen = " + HealthUnconciousDecayOxygen.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("OxygenUnconciousDecayHealth = " + OxygenUnconciousDecayHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("OxygenUnconciousDecayBleed = " + OxygenUnconciousDecayBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("OxygenUnconciousDecayOxygen = " + OxygenUnconciousDecayOxygen.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MinHealthBleedCap = " + MinHealthBleedCap.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureBleedMultiplier = " + CreatureBleedMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourBleedBypassNoDamage = " + (ArmourBleedBypassNoDamage ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourAbsorptionHealth = " + ArmourAbsorptionHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourDirectReductionHealth = " + ArmourDirectReductionHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourResistanceMultiplierHealth = " + ArmourResistanceMultiplierHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourResistancePowerHealth = " + ArmourResistancePowerHealth.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourMinimumHealthPercent = " + ArmourMinimumHealthPercent.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourAbsorptionBleed = " + ArmourAbsorptionBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourDirectReductionBleed = " + ArmourDirectReductionBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourResistanceMultiplierBleed = " + ArmourResistanceMultiplierBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourResistancePowerBleed = " + ArmourResistancePowerBleed.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ArmourMinimumBleedPercent = " + ArmourMinimumBleedPercent.ToString(), ServerLog.MessageType.NilMod);



            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Player related settings
            GameMain.Server.ServerLog.WriteLine("PlayerCanTraumaDeath = " + (PlayerCanTraumaDeath ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCanSuffocateDeath = " + (PlayerCanSuffocateDeath ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCanImplodeDeath = " + (PlayerCanImplodeDeath ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHealthMultiplier = " + PlayerHealthMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHuskHealthMultiplier = " + PlayerHuskHealthMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHuskAiOnDeath = " + PlayerHuskAiOnDeath.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHealthRegen = " + PlayerHealthRegen.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHealthRegenMin = " + (PlayerHealthRegenMin * 100f).ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerHealthRegenMax = " + (PlayerHealthRegenMax * 100f).ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPROnlyWhileUnconcious = " + (PlayerCPROnlyWhileUnconcious ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRHealthBaseValue = " + PlayerCPRHealthBaseValue.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRHealthSkillMultiplier = " + PlayerCPRHealthSkillMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRHealthSkillNeeded = " + PlayerCPRHealthSkillNeeded.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRStunBaseValue = " + PlayerCPRStunBaseValue.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRStunSkillMultiplier = " + PlayerCPRStunSkillMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRStunSkillNeeded = " + PlayerCPRStunSkillNeeded.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRClotBaseValue = " + PlayerCPRClotBaseValue.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRClotSkillMultiplier = " + PlayerCPRClotSkillMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPRClotSkillNeeded = " + PlayerCPRClotSkillNeeded.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPROxygenBaseValue = " + PlayerCPROxygenBaseValue.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerCPROxygenSkillMultiplier = " + PlayerCPROxygenSkillMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("PlayerUnconciousTimer" + PlayerUnconciousTimer.ToString(), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Creature Related Settings
            GameMain.Server.ServerLog.WriteLine("CreatureHealthMultiplier = " + CreatureHealthMultiplier.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureHealthRegen = " + CreatureHealthRegen.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureHealthRegenMin = " + (CreatureHealthRegenMin * 100f).ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureHealthRegenMax = " + (CreatureHealthRegenMax * 100f).ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureEatDyingPlayers = " + (CreatureEatDyingPlayers ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureRespawnMonsterEvents = " + (CreatureRespawnMonsterEvents ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureLimitRespawns = " + (CreatureLimitRespawns ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("CreatureMaxRespawns = " + CreatureMaxRespawns.ToString() + "x spawn", ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            //Server Host Related Settings
            GameMain.Server.ServerLog.WriteLine("PlayYourselfName = " + PlayYourselfName.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("HostBypassSkills = " + (HostBypassSkills ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            if (EnableEventChatterSystem)
            {
                NilMod.NilModEventChatter.ReportSettings();
            }
            else
            {
                GameMain.Server.ServerLog.WriteLine("All elements of NilModEventChatter system are disabled. (No settings to report)", ServerLog.MessageType.NilMod);
            }

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            if (EnableHelpSystem)
            {
                NilMod.NilModHelpCommands.ReportSettings();
            }
            else
            {
                GameMain.Server.ServerLog.WriteLine("All elements of NilModHelpCommands system are disabled. (No help settings to report)", ServerLog.MessageType.NilMod);
            }

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);

            if (EnablePlayerLogSystem)
            {
                NilMod.NilModPlayerLog.ReportSettings();
            }
            else
            {
                GameMain.Server.ServerLog.WriteLine("All elements of NilModPlayerLog system are disabled. (No logs to report)", ServerLog.MessageType.NilMod);
            }

            GameMain.Server.ServerLog.WriteLine("--------------------------------", ServerLog.MessageType.NilMod);
        }

        public void Load()
        {
            //ServerModSetupDefaultServer = false;

            NilModHelpCommands = new NilModHelpCommands();
            NilModEventChatter = new NilModEventChatter();
            NilModPermissions = new NilModPermissions();
            NilModPlayerLog = new PlayerLog();

            XDocument doc = null;

            if (File.Exists(SettingsSavePath))
            {
                ResetToDefault();
                doc = ToolBox.TryLoadXml(SettingsSavePath);
            }
            //We have not actually started once yet, lets reset to current versions default instead without errors.
            else
            {
                ResetToDefault();
                Save();
                doc = ToolBox.TryLoadXml(SettingsSavePath);
            }

            if (doc == null)
            {
                DebugConsole.ThrowError("NilMod config file 'Data/NilModSettings.xml' failed to load - Operating off default settings until resolved.");
                DebugConsole.ThrowError("If you cannot correct the issue above, deleting or renaming the XML and restarting or reloading in-server will generate a new one.");
                ResetToDefault();
            }
            //Load the real data
            else
            {
                //Core Settings
                XElement ServerModGeneralSettings = doc.Root.Element("ServerModGeneralSettings");
                
                BypassMD5 = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BypassMD5", false); //Implemented
                ServerMD5A = ToolBox.GetAttributeString(ServerModGeneralSettings, "ServerMD5A", GameMain.SelectedPackage.MD5hash.Hash); //Implemented
                ServerMD5B = ToolBox.GetAttributeString(ServerModGeneralSettings, "ServerMD5B", GameMain.SelectedPackage.MD5hash.Hash); //Implemented
                DebugConsoleTimeStamp = ClearLogRoundStart = ToolBox.GetAttributeBool(ServerModGeneralSettings, "DebugConsoleTimeStamp", false);
                MaxLogMessages = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "MaxLogMessages", 800), 10,16000); //Implemented
                LogAppendCurrentRound = ToolBox.GetAttributeBool(ServerModGeneralSettings, "LogAppendCurrentRound", false); //Implemented
                LogAppendLineSaveRate = Math.Min(MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "LogAppendLineSaveRate", 5),1,16000), MaxLogMessages); //Implemented
                ClearLogRoundStart = ToolBox.GetAttributeBool(ServerModGeneralSettings, "ClearLogRoundStart", false); //Implemented
                ChatboxHeight = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "ChatboxHeight", 0.15f), 0.10f, 0.50f);
                ChatboxWidth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "ChatboxWidth", 0.35f), 0.25f, 0.85f);
                ChatboxMaxMessages = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "ChatboxMaxMessages", 20), 10,100);
                ShowRoomInfo = ToolBox.GetAttributeBool(ServerModGeneralSettings, "ShowRoomInfo", false);
                UseUpdatedCharHUD = ToolBox.GetAttributeBool(ServerModGeneralSettings, "UseUpdatedCharHUD", false);
                UseCreatureZoomBoost = ToolBox.GetAttributeBool(ServerModGeneralSettings, "UseCreatureZoomBoost", false);
                CreatureZoomMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "CreatureZoomMultiplier", 1f), 0.4f, 3f);
                StartToServer = ToolBox.GetAttributeBool(ServerModGeneralSettings, "StartToServer", false); //Implemented
                EnableEventChatterSystem = ToolBox.GetAttributeBool(ServerModGeneralSettings, "EnableEventChatterSystem", false);
                EnableHelpSystem = ToolBox.GetAttributeBool(ServerModGeneralSettings, "EnableHelpSystem", false);
                EnableAdminSystem = ToolBox.GetAttributeBool(ServerModGeneralSettings, "EnableAdminSystem", false);
                EnablePlayerLogSystem = ToolBox.GetAttributeBool(ServerModGeneralSettings, "EnablePlayerLogSystem", false);
                NilMod.NilModPlayerLog.PlayerLogStateNames = ToolBox.GetAttributeBool(ServerModGeneralSettings, "PlayerLogStateNames", false);
                NilMod.NilModPlayerLog.PlayerLogStateFirstJoinedNames = ToolBox.GetAttributeBool(ServerModGeneralSettings, "PlayerLogStateFirstJoinedNames", false);
                NilMod.NilModPlayerLog.PlayerLogStateLastJoinedNames = ToolBox.GetAttributeBool(ServerModGeneralSettings, "PlayerLogStateLastJoinedNames", false);
                EnableVPNBanlist = ToolBox.GetAttributeBool(ServerModGeneralSettings, "EnableVPNBanlist", false);
                SubVotingConsoleLog = ToolBox.GetAttributeBool(ServerModGeneralSettings, "SubVotingConsoleLog", false);
                SubVotingServerLog = ToolBox.GetAttributeBool(ServerModGeneralSettings, "SubVotingServerLog", false);
                SubVotingAnnounce = ToolBox.GetAttributeBool(ServerModGeneralSettings, "SubVotingAnnounce", false);
                LogAIDamage = ToolBox.GetAttributeBool(ServerModGeneralSettings, "LogAIDamage", false);
                CrashRestart = ToolBox.GetAttributeBool(ServerModGeneralSettings, "CrashRestart", false);
                StartXPos = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "StartXPos", 0), 0, 16000);
                StartYPos = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "StartYPos", 0), 0, 16000);
                UseStartWindowPosition = ToolBox.GetAttributeBool(ServerModGeneralSettings, "UseStartWindowPosition", false);
                BanListReloadTimer = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModGeneralSettings, "BanListReloadTimer", 15), 0, 60);
                BansOverrideBannedInfo = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansOverrideBannedInfo", true);
                BansInfoAddBanName = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansInfoAddBanName", true);
                BansInfoAddBanDuration = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansInfoAddBanDuration", true);
                BansInfoUseRemainingTime = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansInfoUseRemainingTime", true);
                BansInfoAddCustomString = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansInfoAddCustomString", false);
                BansInfoCustomtext = ToolBox.GetAttributeString(ServerModGeneralSettings, "BansInfoCustomtext", "");
                BansInfoAddBanReason = ToolBox.GetAttributeBool(ServerModGeneralSettings, "BansInfoAddBanReason", false);
                VoteKickStateNameTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "VoteKickStateNameTimer", 600f), 0f, 86400f);
                VoteKickDenyRejoinTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "VoteKickDenyRejoinTimer", 60f), 0f, 86400f);
                AdminKickStateNameTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "AdminKickStateNameTimer", 120f), 0f, 86400f);
                AdminKickDenyRejoinTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "AdminKickDenyRejoinTimer", 20f), 0f, 86400f);
                KickStateNameTimerIncreaseOnRejoin = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "KickStateNameTimerIncreaseOnRejoin", 60f), 0f, 86400f);
                KickMaxStateNameTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModGeneralSettings, "KickMaxStateNameTimer", 60f),0f,86400f);
                ClearKickStateNameOnRejoin = ToolBox.GetAttributeBool(ServerModGeneralSettings, "ClearKickStateNameOnRejoin", false);
                ClearKicksOnRoundStart = ToolBox.GetAttributeBool(ServerModGeneralSettings, "ClearKicksOnRoundStart", false);

                //Server Default Settings
                XElement ServerModDefaultServerSettings = doc.Root.Element("ServerModDefaultServerSettings");

                ServerName = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "ServerName", "Barotrauma Server");
                //Sanitize Server Name Code here
                if (ServerName != "")
                {

                    string newservername = ServerName.Trim();
                    ServerName = ServerName.Replace(":", "");
                    ServerName = ServerName.Replace(";", "");
                    if (newservername.Length > 24)
                    {
                        newservername = newservername.Substring(0, 24);
                    }

                    ServerName = newservername;
                }
                if (ServerName.Length < 3)
                {
                    ServerName = "Barotrauma Server";
                    DebugConsole.ThrowError(@"Server name invalid, too short or not supplied. Defaulting to ""Barotrauma Server""");
                }

                ServerPort = Math.Min(Math.Max(ToolBox.GetAttributeInt(ServerModDefaultServerSettings, "ServerPort", 14242), 1025), 65536);

                MaxPlayers = Math.Min(Math.Max(ToolBox.GetAttributeInt(ServerModDefaultServerSettings, "MaxPlayers", 8), 1), 32);
                UseServerPassword = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "UseServerPassword", false);
                ServerPassword = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "ServerPassword", "");
                AdminAuth = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "AdminAuth", "");
                PublicServer = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "PublicServer", true);
                UPNPForwarding = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "UPNPForwarding", false);
                AutoRestart = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "AutoRestart", false);
                DefaultGamemode = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "DefaultGamemode", "Sandbox");
                DefaultMissionType = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "DefaultMissionType", "Random");
                DefaultRespawnShuttle = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "DefaultRespawnShuttle", "");
                DefaultSubmarine = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "DefaultSubmarine", "");
                DefaultLevelSeed = ToolBox.GetAttributeString(ServerModDefaultServerSettings, "DefaultLevelSeed", "");
                SetDefaultsAlways = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "SetDefaultsAlways", false);
                UseAlternativeNetworking = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "UseAlternativeNetworking", false);
                UseCharStatOptimisation = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "UseCharStatOptimisation", true);
                CharacterDisabledistance = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModDefaultServerSettings, "CharacterDisabledistance", 20000.0f), 10000.00f, 100000.00f);
                NetConfig.CharacterIgnoreDistance = CharacterDisabledistance;
                NetConfig.CharacterIgnoreDistanceSqr = CharacterDisabledistance * CharacterDisabledistance;
                ItemPosUpdateDistance = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModDefaultServerSettings, "ItemPosUpdateDistance", 2.00f), 0.25f, 5.00f);
                NetConfig.ItemPosUpdateDistance = ItemPosUpdateDistance;
                DesyncTimerMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModDefaultServerSettings, "DesyncTimerMultiplier", 1.00f), 1.00f, 30.00f);
                DisableParticlesOnStart = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "DisableParticlesOnStart", false);
                DisableLightsOnStart = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "DisableLightsOnStart", false);
                DisableLOSOnStart = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "DisableLOSOnStart", false);
                AllowReconnect = ToolBox.GetAttributeBool(ServerModDefaultServerSettings, "AllowReconnect", false);
                ReconnectAddStun = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModDefaultServerSettings, "ReconnectAddStun", 5.00f), 0.00f, 60.00f);
                ReconnectTimeAllowed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModDefaultServerSettings, "ReconnectTimeAllowed", 10.00f), 10.00f, 600.00f);


                //Debug Settings
                XElement ServerModDebugSettings = doc.Root.Element("ServerModDebugSettings");

                DebugReportSettingsOnLoad = ToolBox.GetAttributeBool(ServerModDebugSettings, "DebugReportSettingsOnLoad", false); //Implemented
                ShowPacketMTUErrors = ToolBox.GetAttributeBool(ServerModDebugSettings, "ShowPacketMTUErrors", true); //Implemented
                ShowOpenALErrors = ToolBox.GetAttributeBool(ServerModDebugSettings, "ShowOpenALErrors", true);
                ShowPathfindingErrors = ToolBox.GetAttributeBool(ServerModDebugSettings, "ShowPathfindingErrors", true);
                ShowMasterServerSuccess = ToolBox.GetAttributeBool(ServerModDebugSettings, "ShowMasterServerSuccess", true);
                DebugLag = ToolBox.GetAttributeBool(ServerModDebugSettings, "DebugLag", false);
                DebugLagSimulatedPacketLoss = ToolBox.GetAttributeFloat(ServerModDebugSettings, "DebugLagSimulatedPacketLoss", 0.05f); //Implemented
                DebugLagSimulatedRandomLatency = ToolBox.GetAttributeFloat(ServerModDebugSettings, "DebugLagSimulatedRandomLatency", 0.05f); //Implemented
                DebugLagSimulatedDuplicatesChance = ToolBox.GetAttributeFloat(ServerModDebugSettings, "DebugLagSimulatedDuplicatesChance", 0.05f); //Implemented
                DebugLagSimulatedMinimumLatency = ToolBox.GetAttributeFloat(ServerModDebugSettings, "DebugLagSimulatedMinimumLatency", 0.10f); //Implemented
                DebugLagConnectionTimeout = ToolBox.GetAttributeFloat(ServerModDebugSettings, "DebugLagConnectionTimeout", 60f); //Implemented
                MaxParticles = Math.Min(Math.Max(ToolBox.GetAttributeInt(ServerModDebugSettings, "MaxParticles", 1500), 150), 15000);
                ParticleSpawnPercent = Math.Min(Math.Max(ToolBox.GetAttributeInt(ServerModDebugSettings, "ParticleSpawnPercent", 100), 0), 100);
                MathHelper.Clamp(ParticleLifeMultiplier = ToolBox.GetAttributeFloat(ServerModDebugSettings, "ParticleLifeMultiplier", 1f),0.15f,1f); //Implemented

#if CLIENT
                if (GameMain.ParticleManager != null)
                {
                    GameMain.ParticleManager.ResetParticleManager();
                }
#endif

                //Respawn Settings
                XElement ServerModRespawnSettings = doc.Root.Element("ServerModRespawnSettings");

                LimitCharacterRespawns = ToolBox.GetAttributeBool(ServerModRespawnSettings, "LimitCharacterRespawns", false); //Unimplemented
                LimitShuttleRespawns = ToolBox.GetAttributeBool(ServerModRespawnSettings, "LimitShuttleRespawns", false); //Unimplemented
                MaxRespawnCharacters = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModRespawnSettings, "MaxRespawnCharacters", -1), -1, 10000);
                MaxRespawnShuttles = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModRespawnSettings, "MaxRespawnShuttles", -1), -1, 1000);
                BaseRespawnCharacters = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModRespawnSettings, "BaseRespawnCharacters", 0f), 0f, 10000f);
                BaseRespawnShuttles = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModRespawnSettings, "BaseRespawnShuttles", 0f), 0f, 1000f);
                RespawnCharactersPerPlayer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModRespawnSettings, "RespawnCharactersPerPlayer", 0f), 0f, 10000f);
                RespawnShuttlesPerPlayer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModRespawnSettings, "RespawnShuttlesPerPlayer", 0f), 0f, 1000f);
                AlwaysRespawnNewConnections = ToolBox.GetAttributeBool(ServerModRespawnSettings, "AlwaysRespawnNewConnections", false); //Unimplemented
                RespawnNewConnectionsToSub = ToolBox.GetAttributeBool(ServerModRespawnSettings, "RespawnNewConnectionsToSub", false); //Unimplemented
                RespawnOnMainSub = ToolBox.GetAttributeBool(ServerModRespawnSettings, "RespawnOnMainSub", false); //Implemented
                RespawnWearingSuitGear = ToolBox.GetAttributeBool(ServerModRespawnSettings, "RespawnWearingSuitGear", false);
                RespawnLeavingAutoPilotMode = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModRespawnSettings, "RespawnLeavingAutoPilotMode", 0),0,3);
                RespawnShuttleLeavingCloseDoors = ToolBox.GetAttributeBool(ServerModRespawnSettings, "RespawnShuttleLeavingCloseDoors", true);
                RespawnShuttleLeavingUndock = ToolBox.GetAttributeBool(ServerModRespawnSettings, "RespawnShuttleLeavingUndock", true);
                RespawnShuttleLeaveAtTime = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModRespawnSettings, "RespawnShuttleLeaveAtTime", -1f), -1f, 600f);

                //Submarine Settings
                XElement ServerModSubmarineSettings = doc.Root.Element("ServerModSubmarineSettings");

                HullOxygenDistributionSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "HullOxygenDistributionSpeed", 500f),0f,50000f); //Implemented
                HullOxygenDetoriationSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "HullOxygenDetoriationSpeed", 0.3f), -10000f, 50000f); //Implemented
                HullOxygenConsumptionSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "HullOxygenConsumptionSpeed", 1000f), 0f, 50000f); //Implemented
                HullUnbreathablePercent = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "HullUnbreathablePercent", 30.0f),0f,100f); //Implemented
                CanDamageSubBody = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "CanDamageSubBody", true); //Implemented
                CanRewireMainSubs = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "CanRewireMainSubs", true); //Not Implemented.
                CrushDamageDepth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "CrushDamageDepth", -30000f),-1000000f,100000f);
                PlayerCrushDepthInHull = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PlayerCrushDepthInHull", -30000f), -1000000f, 100000f);
                PlayerCrushDepthOutsideHull = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PlayerCrushDepthOutsideHull", -30000f), -1000000f, 100000f);
                UseProgressiveCrush = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "UseProgressiveCrush", false);
                PCrushUseWallRemainingHealthCheck = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "PCrushUseWallRemainingHealthCheck", false);
                PCrushDepthHealthResistMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushDepthHealthResistMultiplier", 1.0f),0f,100.0f);
                PCrushDepthBaseHealthResist = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushDepthBaseHealthResist", 0f), 0f, 1000000f);
                PCrushDamageDepthMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushDamageDepthMultiplier", 1.0f), 0f, 100.0f);
                PCrushBaseDamage = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushBaseDamage", 0f), 0f, 100000000f);
                PCrushWallHealthDamagePercent = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushWallHealthDamagePercent", 0f), 0f, 100f);
                PCrushWallBaseDamageChance = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushWallBaseDamageChance", 35f), 0f, 100f);
                PCrushWallDamageChanceIncrease = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushWallDamageChanceIncrease", 5f), 0f, 1000f);
                PCrushWallMaxDamageChance = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushWallMaxDamageChance", 100f), 0f, 100f);
                PCrushInterval = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "PCrushInterval", 10f), 0.5f, 60f);
                SyncFireSizeChange = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "SyncFireSizeChange", false);
                FireSyncFrequency = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireSyncFrequency", 4f), 1f, 60f);
                FireSizeChangeToSync = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireSizeChangeToSync", 6f), 1f, 30f);
                FireProofDamagePercentReduction = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireProofDamagePercentReduction", 100f), 0f, 100f);
                FireProofRangePercentReduction = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireProofRangePercentReduction", 0f), 0f, 100f);
                FireCharDamageMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireCharDamageMultiplier", 1f), 0f, 100f);
                FireCharRangeMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireCharRangeMultiplier", 1f), 0f, 100f);
                FireItemRangeMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireItemRangeMultiplier", 1f), 0f, 100f);
                FireUseRangedDamage = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "FireUseRangedDamage", false);
                FireRangedDamageStrength = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireRangedDamageStrength", 1.0f), -1f, 10f);
                FireRangedDamageMinMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireRangedDamageMinMultiplier", 0.05f), 0f, 2f);
                FireRangedDamageMaxMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireRangedDamageMaxMultiplier", 1f), FireRangedDamageMinMultiplier, 100f);
                FireOxygenConsumption = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireOxygenConsumption", 50f), 0f, 50000f);
                FireGrowthSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireGrowthSpeed", 5f), 0.1f, 1000f);
                FireShrinkSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireShrinkSpeed", 5f), 0.1f, 1000f);
                FireWaterExtinguishMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireWaterExtinguishMultiplier", 1f), 0.5f, 60f);
                FireToolExtinguishMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "FireToolExtinguishMultiplier", 1f), 0.5f, 60f);
                EnginesRegenerateCondition = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "EnginesRegenerateCondition", false);
                EnginesRegenAmount = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "EnginesRegenAmount", 0f), 0f, 1000f);
                ElectricalRegenerateCondition = ToolBox.GetAttributeBool(ServerModSubmarineSettings, "ElectricalRegenerateCondition", false);
                ElectricalRegenAmount = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalRegenAmount", 0f), 0f, 1000f);
                ElectricalOverloadDamage = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalOverloadDamage", 10f), 0f, 60f);
                ElectricalOverloadMinPower = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalOverloadMinPower", 200f), 0f, 100000f);
                ElectricalOverloadVoltRangeMin = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalOverloadVoltRangeMin", 1.9f), 0f, 60f);
                ElectricalOverloadVoltRangeMax = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalOverloadVoltRangeMax", 2.1f), ElectricalOverloadVoltRangeMin, 60f);
                ElectricalOverloadFiresChance = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalOverloadFiresChance", 100f), 0.5f, 60f);
                ElectricalFailMaxVoltage = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalFailMaxVoltage", 0.1f), 0f, 100f);
                ElectricalFailStunTime = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModSubmarineSettings, "ElectricalFailStunTime", 5f), 0.1f, 60f);


                //All Character Settings
                XElement ServerModAllCharacterSettings = doc.Root.Element("ServerModAllCharacterSettings");

                PlayerOxygenUsageAmount = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "PlayerOxygenUsageAmount", -5.0f), -400f, 20f); //Implemented
                PlayerOxygenGainSpeed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "PlayerOxygenGainSpeed", 10.0f), -20f, 400f); //Implemented
                UseProgressiveImplodeDeath = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "UseProgressiveImplodeDeath", false);
                ImplodeHealthLoss = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ImplodeHealthLoss", 0.35f), 0f, 1000000f);
                ImplodeBleedGain = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ImplodeBleedGain", 0.12f), 0f, 1000000f);
                ImplodeOxygenLoss = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ImplodeOxygenLoss", 1.5f), 0f, 1000000f);
                PreventImplodeHealing = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "PreventImplodeHealing", false);
                PreventImplodeClotting = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "PreventImplodeClotting", false);
                PreventImplodeOxygen = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "PreventImplodeOxygen", false);
                CharacterImplodeDeathAtMinHealth = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "CharacterImplodeDeathAtMinHealth", true);
                HuskHealingMultiplierinfected = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "HuskHealingMultiplierinfected", 1.0f), -1000f, 1000f);
                HuskHealingMultiplierincurable = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "HuskHealingMultiplierincurable", 1.0f), -1000f, 1000f);
                PlayerHuskInfectedDrain = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "PlayerHuskInfectedDrain", 0.00f), -1000f, 1000f);
                PlayerHuskIncurableDrain = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "PlayerHuskIncurableDrain", 0.50f), -1000f, 1000f);
                HealthUnconciousDecayHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "HealthUnconciousDecayHealth", 0.5f),-500f,200f); //Implemented
                HealthUnconciousDecayBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "HealthUnconciousDecayBleed", 0.0f), -500f, 200f); //Implemented
                HealthUnconciousDecayOxygen = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "HealthUnconciousDecayOxygen", 0.0f),-100f,200f); //Implemented
                OxygenUnconciousDecayHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "OxygenUnconciousDecayHealth", 0.0f), -500f, 200f); //Implemented
                OxygenUnconciousDecayBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "OxygenUnconciousDecayBleed", 0.0f), -500f, 200f); //Implemented
                OxygenUnconciousDecayOxygen = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "OxygenUnconciousDecayOxygen", 0.0f), -100f, 200f); //Implemented
                MinHealthBleedCap = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "MinHealthBleedCap", 5f),0f,5f); //Implemented
                CreatureBleedMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "CreatureBleedMultiplier", 1.00f),0f,20f);
                ArmourBleedBypassNoDamage = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "ArmourBleedBypassNoDamage", false);
                ArmourSoakFireDamage = ToolBox.GetAttributeBool(ServerModAllCharacterSettings, "ArmourSoakFireDamage", false);
                ArmourAbsorptionHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourAbsorptionHealth", 0f), 0f, 10000f);
                ArmourDirectReductionHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourDirectReductionHealth", 1f), 0f, 10000f);
                ArmourMinimumHealthPercent = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourMinimumHealthPercent", 0f), 0f, 100f);
                ArmourResistancePowerHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourResistancePowerHealth", 0f), 0f, 1f);
                ArmourResistanceMultiplierHealth = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourResistanceMultiplierHealth", 0f), 0f, 100000f);
                ArmourAbsorptionBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourAbsorptionBleed", 0f), 0f, 10000f);
                ArmourDirectReductionBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourDirectReductionBleed", 1f), 0f, 10000f);
                ArmourResistancePowerBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourResistancePowerBleed", 0f), 0f, 1f);
                ArmourResistanceMultiplierBleed = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourResistanceMultiplierBleed", 0f), 0f, 100000f);
                ArmourMinimumBleedPercent = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAllCharacterSettings, "ArmourMinimumBleedPercent", 0f), 0f, 100f);




                //Player Settings
                XElement ServerModPlayerSettings = doc.Root.Element("ServerModPlayerSettings");

                PlayerCanTraumaDeath = ToolBox.GetAttributeBool(ServerModPlayerSettings, "PlayerCanTraumaDeath", true); //Implemented
                PlayerCanImplodeDeath = ToolBox.GetAttributeBool(ServerModPlayerSettings, "PlayerCanImplodeDeath", true); //Implemented
                PlayerCanSuffocateDeath = ToolBox.GetAttributeBool(ServerModPlayerSettings, "PlayerCanSuffocateDeath", true); //Implemented
                PlayerHealthMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerHealthMultiplier", 1f), 0.01f, 10000f); //Implemented
                PlayerHuskHealthMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerHuskHealthMultiplier", 1f), 0.01f, 10000f); //Implemented
                PlayerHuskAiOnDeath = ToolBox.GetAttributeBool(ServerModPlayerSettings, "PlayerHuskAiOnDeath", true); //Implemented
                PlayerHealthRegen = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerHealthRegen", 0f), 0f, 10000000f); //Implemented
                PlayerHealthRegenMin = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerHealthRegenMin", -100f), -100f, 100f) / 100f; //Implemented
                PlayerHealthRegenMax = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerHealthRegenMax", 100f), -100f, 100f) / 100f; //Implemented
                PlayerCPROnlyWhileUnconcious = ToolBox.GetAttributeBool(ServerModPlayerSettings, "PlayerCPROnlyWhileUnconcious", true); //Implemented
                PlayerCPRHealthBaseValue = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRHealthBaseValue", 0f),-1000f,1000f); //Implemented
                PlayerCPRHealthSkillMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRHealthSkillMultiplier", 0f), -10f, 100f); //Implemented
                PlayerCPRHealthSkillNeeded = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModPlayerSettings, "PlayerCPRHealthSkillNeeded", 100), 0, 100); //Implemented
                PlayerCPRStunBaseValue = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRStunBaseValue", 0f), -1000f, 1000f); //Implemented
                PlayerCPRStunSkillMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRStunSkillMultiplier", 0f), -10f, 100f); //Implemented
                PlayerCPRStunSkillNeeded = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModPlayerSettings, "PlayerCPRStunSkillNeeded", 0), 0, 100); //Implemented
                PlayerCPRClotBaseValue = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRClotBaseValue", 0f), -1000f, 1000f); //Implemented
                PlayerCPRClotSkillMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPRClotSkillMultiplier", 0f), -10f, 100f); //Implemented
                PlayerCPRClotSkillNeeded = MathHelper.Clamp(ToolBox.GetAttributeInt(ServerModPlayerSettings, "PlayerCPRClotSkillNeeded", 0), 0, 100); //Implemented
                PlayerCPROxygenBaseValue = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPROxygenBaseValue", 0f), -1000f, 1000f); //Implemented
                PlayerCPROxygenSkillMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerCPROxygenSkillMultiplier", 0.1f), -10f, 100f); //Implemented
                PlayerUnconciousTimer = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModPlayerSettings, "PlayerUnconciousTimer", 5f), 0f, 60f); //Implemented //Implemented

                //Host Specific settings
                XElement ServerModHostSettings = doc.Root.Element("ServerModHostSettings");
                
                PlayYourselfName = ToolBox.GetAttributeString(ServerModHostSettings, "PlayYourselfName", ""); //Implemented
                HostBypassSkills = ToolBox.GetAttributeBool(ServerModHostSettings, "HostBypassSkills", false); //Implemented

                //Creature specific settings
                XElement ServerModAICreatureSettings = doc.Root.Element("ServerModAICreatureSettings");

                CreatureHealthMultiplier = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAICreatureSettings, "CreatureHealthMultiplier", 1f),0.01f,1000000f);
                CreatureHealthRegen = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAICreatureSettings, "CreatureHealthRegen", 0f), 0f, 10000000f); //Implemented
                CreatureHealthRegenMin = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAICreatureSettings, "CreatureHealthRegenMin", 0f), 0f, 100f) / 100f;
                CreatureHealthRegenMax = MathHelper.Clamp(ToolBox.GetAttributeFloat(ServerModAICreatureSettings, "CreatureHealthRegenMax", 100f), 0f, 100f) / 100f;
                CreatureEatDyingPlayers = ToolBox.GetAttributeBool(ServerModAICreatureSettings, "CreatureEatDyingPlayers", true); //Implemented
                CreatureRespawnMonsterEvents = ToolBox.GetAttributeBool(ServerModAICreatureSettings, "CreatureRespawnMonsterEvents", true);
                CreatureLimitRespawns = ToolBox.GetAttributeBool(ServerModAICreatureSettings, "CreatureLimitRespawns", false);
                CreatureMaxRespawns = Math.Min(Math.Max(ToolBox.GetAttributeInt(ServerModAICreatureSettings, "CreatureMaxRespawns", 1), 2), 50);


                //Sanitize the hosts name
                if (PlayYourselfName != "")
                {
                    string newhostname = PlayYourselfName.Trim();
                    if (newhostname.Length > 20)
                    {
                        newhostname = newhostname.Substring(0, 20);
                    }
                    string newhostrName = "";
                    for (int i = 0; i < newhostname.Length; i++)
                    {
                        if (newhostname[i] < 32)
                        {
                            newhostrName += '?';
                        }
                        else
                        {
                            newhostrName += newhostname[i];
                        }
                    }
                    PlayYourselfName = newhostname;
                }

                //Default Server Creatures to spawn

                XElement NilModDefaultServerSpawnsdoc = doc.Root.Element("NilModDefaultServerSpawns");


                //Additional Cargo Spawning defaults
                XElement NilModAdditionalCargodoc = doc.Root.Element("NilModAdditionalCargo");

                LoadComponants();

                //Just Disable everything in the class despite whatever it loaded. simples
                if (!EnableEventChatterSystem)
                {
                    NilMod.NilModEventChatter.ChatCargo = false;
                    NilMod.NilModEventChatter.ChatModServerJoin = false;
                    NilMod.NilModEventChatter.ChatMonster = false;
                    NilMod.NilModEventChatter.ChatNoneTraitorReminder = false;
                    NilMod.NilModEventChatter.ChatSalvage = false;
                    NilMod.NilModEventChatter.ChatSandbox = false;
                    NilMod.NilModEventChatter.ChatShuttleLeavingKill = false;
                    NilMod.NilModEventChatter.ChatShuttleRespawn = false;
                    NilMod.NilModEventChatter.ChatSubvsSub = false;
                    NilMod.NilModEventChatter.ChatTraitorReminder = false;
                    NilMod.NilModEventChatter.ChatVoteEnd = false;
                }
            }

            //Only if we actually loaded the mod, save over it.
            if (doc != null)
            {
                Save();
            }
        }

        public void Save()
        {
            List<string> lines = new List<string>
            {
                @"<?xml version=""1.0"" encoding=""utf-8"" ?>",
                "<NilMod>",
                "  <!--This is advanced configuration settings for your server stored by this modification!-->",

                "",

                "  <!--NilMod comes with a variety of additional commands, functionality and features, Below you can configure them:-->",
                "  <!--Regarding the BypassMD5 function, DO NOT use this to add content such as new creatures/items/missions etc, players will NOT sync data they do not have themselves-->",
                "  <!--Additionally do NOT edit randomevents.xml or missions.xml or Level generation as these NEVER sync as the client calculates such themselves based of level seed -->",
                "  <!--Finally do not edit the Human Oxygen or Health values as these cause desynced health/oxygen levels, doesbleed / bleedingdecreasespeed / Speed and other stats may be ok however test it yourself-->",
                "  <!--MD5 Desync editing on the server content folder is intended for tweaking monster stats, AI operation (Such as sight range / targetting in their creature file)-->",
                "  <!--Modifying existing status effects or adding new Status Effects to items, Damage/Bleed damage, creature speeds, or general small tweak experimentation-->",
                "  <!--Once you have made any changes join with another client (So you fail to join due to MD5) and read the clients MD5, enter that in here to allow connection for unmodified clients of that content package-->",
                "  <!--You could even try other content package mods, Such as Barotrauma Extended and tweak server-sided values-->",

                "",

                "  <!--BypassMD5 = Setting to change server from calculating MD5 normally to using the ServerMD5 setting file instead, Default=false-->",
                "  <!--ServerMD5 = The server content folders MD5 if unmodified - used for players using the same content mod to connect despite edits, Default= Unknown, try connecting with a client after edits and change then-->",
                "  <!--SuppressPacketSizeWarning = Suppresses an error that annoys server hosts but is actually mostly harmless anyways, Default=false-->",
                "  <!--StartToServer = Setting to use the server default settings when starting NilMod - for now please use actually valid settings XD, Default=false-->",

                "",

                //"  <!--MaxRespawns = number of times the respawn shuttle may appear before it ceases to spawn 0=Infinite, default=0-->",
                "  <!--RespawnOnMainSub = Instantly places newly respawned players into the mainsub at an appropriate spawn point, good for Deathmatching, Default=false-->",
                "",
                "  <!--PlayerCanDie = Experiment feature to allow players to be permanently revivable unless they Give in, 6.0.2 currenty has a clientside GUI issue preventing 'Give in' during pressure death however-->",
                "  <!--PlayerHealthRegen = quantity (As a decimal value per second) a still-living Concious players health regenerates naturally, default=0-->",
                "  <!--CreatureHealthRegen = quantity (As a decimal value per second) a still-living Creatures health regenerates naturally, default=0-->",
                "  <!--UnconciousHealthDecay = Amount of health loss per second if you run out of oxygen or health, default=0.5-->",
                "  <!--UnconciousOxygenDecay = Amount of oxygen loss per second if you run out of oxygen or health, default=0.5-->",
                "  <!--MinHealthBleedCap = When an unconcious player reaches their minimum (Most negative possible) health, Reset the bleeding to this amount (0.0-5.0), Default=2-->",
                "  <!--PlayerUnconciousTimer = The Stun duration for when a player crosses from unconcious to concious, smaller values pickup players faster, Default=5-->",

                "",

                "  <!--HullOxygenDistributionSpeed = Rate at which a rooms oxygen moves from one room to another, Default=500-->",
                "  <!--HullOxygenDetoriationSpeed = Rate at which a room oxygen slowly decays, Default=0.3-->",
                "  <!--HullOxygenConsumptionSpeed = Rate at which a player or creature removes oxygen from a room, Default=1000-->",
                "  <!--CanDamageSubBody = if false this allows the submarine hull to be in 'Godmode' regardless of godmode, while players and creatures are still harmed normally, Default=true-->",

                "",

                "  <!--PlayerOxygenUsageAmount = Amount of oxygen a player consumes when they cannot breathe - Effectively the rate the blue bar drops, Default=-5-->",
                "  <!--PlayerOxygenGainSpeed = Amount of oxygen a player gains when they can breathe - Effectively the rate the blue bar increases, Default=10-->",
                "  <!--UnbreathablePercent = A scale of 0 % to 100 % of oxygen left in room before it is considered 'Unbreathable' and switches PlayerOxygenGainSpeed to PlayerOxygenUsageAmount, Default=30-->",

                "",

                "  <!--PlayYourselfName = Name for your character that clients will see correctly, Default= (Uses server name if left as empty string)-->",
                "  <!--HostBypassSkills = Allows anything you control to have 100 skill for any skillcheck regardless of job (Others won't see this on crew tab too) - Allows husks and spawned human AI's / play yourself to do anything, Default=false-->",

                "",

                "  <ServerModGeneralSettings",
                @"    BypassMD5=""" + BypassMD5 + @"""",
                @"    ServerMD5A=""" + ServerMD5A + @"""",
                @"    ServerMD5B=""" + ServerMD5B + @"""",
                @"    DebugConsoleTimeStamp=""" + DebugConsoleTimeStamp + @"""",
                @"    MaxLogMessages=""" + MaxLogMessages + @"""",
                @"    LogAppendCurrentRound=""" + LogAppendCurrentRound + @"""",
                @"    LogAppendLineSaveRate=""" + LogAppendLineSaveRate + @"""",
                @"    ClearLogRoundStart=""" + ClearLogRoundStart + @"""",
                @"    ChatboxHeight=""" + ChatboxHeight + @"""",
                @"    ChatboxWidth=""" + ChatboxWidth + @"""",
                @"    ChatboxMaxMessages=""" + ChatboxMaxMessages + @"""",
                @"    ShowRoomInfo=""" + ShowRoomInfo + @"""",
                @"    UseUpdatedCharHUD=""" + UseUpdatedCharHUD + @"""",
                @"    UseCreatureZoomBoost=""" + UseCreatureZoomBoost + @"""",
                @"    CreatureZoomMultiplier=""" + CreatureZoomMultiplier + @"""",
                @"    StartToServer=""" + StartToServer + @"""",
                @"    EnableEventChatterSystem=""" + EnableEventChatterSystem + @"""",
                @"    EnableHelpSystem=""" + EnableHelpSystem + @"""",
                //@"    EnableAdminSystem=""" + EnableAdminSystem + @"""",
                @"    EnablePlayerLogSystem=""" + EnablePlayerLogSystem + @"""",
                @"    PlayerLogStateNames=""" + NilModPlayerLog.PlayerLogStateNames + @"""",
                @"    PlayerLogStateFirstJoinedNames=""" + NilModPlayerLog.PlayerLogStateFirstJoinedNames + @"""",
                @"    PlayerLogStateLastJoinedNames=""" + NilModPlayerLog.PlayerLogStateLastJoinedNames + @"""",
                @"    EnableVPNBanlist=""" + EnableVPNBanlist + @"""",
                @"    SubVotingConsoleLog=""" + SubVotingConsoleLog + @"""",
                @"    SubVotingServerLog=""" + SubVotingServerLog + @"""",
                @"    SubVotingAnnounce=""" + SubVotingAnnounce + @"""",
                @"    LogAIDamage=""" + LogAIDamage + @"""",
                @"    CrashRestart=""" + CrashRestart + @"""",
                @"    UseStartWindowPosition=""" + UseStartWindowPosition + @"""",
                @"    StartXPos=""" + StartXPos + @"""",
                @"    StartYPos=""" + StartYPos + @"""",
                @"    BanListReloadTimer=""" + BanListReloadTimer + @"""",
                @"    BansOverrideBannedInfo=""" + BansOverrideBannedInfo + @"""",
                @"    BansInfoCustomtext=""" + BansInfoCustomtext + @"""",
                @"    BansInfoAddBanName=""" + BansInfoAddBanName + @"""",
                @"    BansInfoAddBanDuration=""" + BansInfoAddBanDuration + @"""",
                @"    BansInfoUseRemainingTime=""" + BansInfoUseRemainingTime + @"""",
                @"    BansInfoAddCustomString=""" + BansInfoAddCustomString + @"""",
                @"    BansInfoAddBanReason=""" + BansInfoAddBanReason + @"""",
                @"    VoteKickStateNameTimer=""" + VoteKickStateNameTimer + @"""",
                @"    VoteKickDenyRejoinTimer=""" + VoteKickDenyRejoinTimer + @"""",
                @"    AdminKickStateNameTimer=""" + AdminKickStateNameTimer + @"""",
                @"    AdminKickDenyRejoinTimer=""" + AdminKickDenyRejoinTimer + @"""",
                @"    KickStateNameTimerIncreaseOnRejoin=""" + KickStateNameTimerIncreaseOnRejoin + @"""",
                @"    KickMaxStateNameTimer=""" + KickMaxStateNameTimer + @"""",
                @"    ClearKickStateNameOnRejoin=""" + ClearKickStateNameOnRejoin + @"""",
                @"    ClearKicksOnRoundStart=""" + ClearKicksOnRoundStart + @"""",
                "  />",

                "",

                "  <!--If Barotrauma is started with -startserver OR StartToServer above is true, allows to immediately start the server with the following setup-->",
                "  <ServerModDefaultServerSettings",
                @"    ServerName=""" + ServerName + @"""",
                @"    ServerPort=""" + ServerPort + @"""",
                @"    MaxPlayers=""" + MaxPlayers + @"""",
                @"    UseServerPassword=""" + UseServerPassword + @"""",
                @"    ServerPassword=""" + ServerPassword + @"""",
                @"    AdminAuth=""" + AdminAuth + @"""",
                @"    PublicServer=""" + PublicServer + @"""",
                @"    UPNPForwarding=""" + UPNPForwarding + @"""",
                @"    AutoRestart=""" + AutoRestart + @"""",
                @"    DefaultGamemode=""" + DefaultGamemode + @"""",
                @"    DefaultMissionType=""" + DefaultMissionType + @"""",
                @"    DefaultRespawnShuttle=""" + DefaultRespawnShuttle + @"""",
                @"    DefaultSubmarine=""" + DefaultSubmarine + @"""",
                @"    DefaultLevelSeed=""" + DefaultLevelSeed + @"""",
                //@"    SetDefaultsAlways=""" + SetDefaultsAlways + @"""",
                @"    UseAlternativeNetworking=""" + UseAlternativeNetworking + @"""",
                //@"    UseCharStatOptimisation=""" + UseCharStatOptimisation + @"""",
                @"    CharacterDisabledistance=""" + CharacterDisabledistance + @"""",
                @"    ItemPosUpdateDistance=""" + ItemPosUpdateDistance + @"""",
                @"    DesyncTimerMultiplier=""" + DesyncTimerMultiplier + @"""",
                @"    DisableParticlesOnStart=""" + DisableParticlesOnStart + @"""",
                @"    DisableLightsOnStart=""" + DisableLightsOnStart + @"""",
                @"    DisableLOSOnStart=""" + DisableLOSOnStart + @"""",
                @"    AllowReconnect=""" + AllowReconnect + @"""",
                @"    ReconnectAddStun=""" + ReconnectAddStun + @"""",
                @"    ReconnectTimeAllowed=""" + ReconnectTimeAllowed + @"""",
                "  />",

                "",

                "  <!--Debug Options for error management and testing-->",
                "  <ServerModDebugSettings",
                @"    DebugReportSettingsOnLoad=""" + DebugReportSettingsOnLoad + @"""",
                @"    ShowPacketMTUErrors=""" + ShowPacketMTUErrors + @"""",
                @"    ShowOpenALErrors=""" + ShowOpenALErrors + @"""",
                @"    ShowPathfindingErrors=""" + ShowPathfindingErrors + @"""",
                @"    ShowMasterServerSuccess=""" + ShowMasterServerSuccess + @"""",
                @"    DebugLag=""" + DebugLag + @"""",
                @"    DebugLagSimulatedPacketLoss=""" + DebugLagSimulatedPacketLoss + @"""",
                @"    DebugLagSimulatedRandomLatency=""" + DebugLagSimulatedRandomLatency + @"""",
                @"    DebugLagSimulatedDuplicatesChance=""" + DebugLagSimulatedDuplicatesChance + @"""",
                @"    DebugLagSimulatedMinimumLatency=""" + DebugLagSimulatedMinimumLatency + @"""",
                @"    DebugLagConnectionTimeout=""" + DebugLagConnectionTimeout + @"""",
                @"    MaxParticles=""" + MaxParticles + @"""",
                @"    ParticleSpawnPercent=""" + ParticleSpawnPercent + @"""",
                @"    ParticleLifeMultiplier=""" + ParticleLifeMultiplier + @"""",
                "  />",

                "",

                "  <!--These Settings are related to respawning manager-->",
                "  <ServerModRespawnSettings",
                //@"    LimitCharacterRespawns=""" + LimitCharacterRespawns + @"""",
                //@"    LimitShuttleRespawns=""" + LimitShuttleRespawns + @"""",
                //@"    MaxRespawnCharacters=""" + MaxRespawnCharacters + @"""",
                //@"    MaxRespawnShuttles=""" + MaxRespawnShuttles + @"""",
                //@"    BaseRespawnCharacters=""" + BaseRespawnCharacters + @"""",
                //@"    BaseRespawnShuttles=""" + BaseRespawnShuttles + @"""",
                //@"    RespawnCharactersPerPlayer=""" + RespawnCharactersPerPlayer + @"""",
                //@"    RespawnShuttlesPerPlayer=""" + RespawnShuttlesPerPlayer + @"""",
                //@"    AlwaysRespawnNewConnections=""" + AlwaysRespawnNewConnections + @"""",
                //@"    RespawnNewConnectionsToSub=""" + RespawnNewConnectionsToSub + @"""",
                @"    RespawnOnMainSub=""" + RespawnOnMainSub + @"""",
                @"    RespawnLeavingAutoPilotMode=""" + RespawnLeavingAutoPilotMode + @"""",
                @"    RespawnShuttleLeavingCloseDoors=""" + RespawnShuttleLeavingCloseDoors + @"""",
                @"    RespawnShuttleLeavingUndock=""" + RespawnShuttleLeavingUndock + @"""",
                @"    RespawnShuttleLeaveAtTime=""" + RespawnShuttleLeaveAtTime + @"""",
                "  />",

                "",

                "  <ServerModSubmarineSettings",
                @"    HullOxygenDistributionSpeed=""" + HullOxygenDistributionSpeed + @"""",
                @"    HullOxygenDetoriationSpeed=""" + HullOxygenDetoriationSpeed + @"""",
                @"    HullOxygenConsumptionSpeed=""" + HullOxygenConsumptionSpeed + @"""",
                @"    HullUnbreathablePercent=""" + HullUnbreathablePercent + @"""",
                @"    CanDamageSubBody=""" + CanDamageSubBody + @"""",
                //@"    CanRewireMainSubs=""" + CanRewireMainSubs + @"""",
                @"    CrushDamageDepth=""" + CrushDamageDepth + @"""",
                @"    PlayerCrushDepthInHull=""" + PlayerCrushDepthInHull + @"""",
                @"    PlayerCrushDepthOutsideHull=""" + PlayerCrushDepthOutsideHull + @"""",
                @"    UseProgressiveCrush=""" + UseProgressiveCrush + @"""",
                @"    PCrushUseWallRemainingHealthCheck=""" + PCrushUseWallRemainingHealthCheck + @"""",
                @"    PCrushDepthHealthResistMultiplier=""" + PCrushDepthHealthResistMultiplier + @"""",
                @"    PCrushDepthBaseHealthResist=""" + PCrushDepthBaseHealthResist + @"""",
                @"    PCrushDamageDepthMultiplier=""" + PCrushDamageDepthMultiplier + @"""",
                @"    PCrushBaseDamage=""" + PCrushBaseDamage + @"""",
                @"    PCrushWallHealthDamagePercent=""" + PCrushWallHealthDamagePercent + @"""",
                @"    PCrushWallBaseDamageChance=""" + PCrushWallBaseDamageChance + @"""",
                @"    PCrushWallDamageChanceIncrease=""" + PCrushWallDamageChanceIncrease + @"""",
                @"    PCrushWallMaxDamageChance=""" + PCrushWallMaxDamageChance + @"""",
                @"    PCrushInterval=""" + PCrushInterval + @"""",
                @"    SyncFireSizeChange=""" + SyncFireSizeChange + @"""",
                @"    FireSyncFrequency=""" + FireSyncFrequency + @"""",
                @"    FireSizeChangeToSync=""" + FireSizeChangeToSync + @"""",
                @"    FireProofDamagePercentReduction=""" + FireProofDamagePercentReduction + @"""",
                @"    FireProofRangePercentReduction=""" + FireProofRangePercentReduction + @"""",
                @"    FireCharDamageMultiplier=""" + FireCharDamageMultiplier + @"""",
                @"    FireCharRangeMultiplier=""" + FireCharRangeMultiplier + @"""",
                @"    FireItemRangeMultiplier=""" + FireItemRangeMultiplier + @"""",
                @"    FireUseRangedDamage=""" + FireUseRangedDamage + @"""",
                @"    FireRangedDamageStrength=""" + FireRangedDamageStrength + @"""",
                @"    FireRangedDamageMinMultiplier=""" + FireRangedDamageMinMultiplier + @"""",
                @"    FireRangedDamageMaxMultiplier=""" + FireRangedDamageMaxMultiplier + @"""",
                @"    FireOxygenConsumption=""" + FireOxygenConsumption + @"""",
                @"    FireGrowthSpeed=""" + FireGrowthSpeed + @"""",
                @"    FireShrinkSpeed=""" + FireShrinkSpeed + @"""",
                @"    FireWaterExtinguishMultiplier=""" + FireWaterExtinguishMultiplier + @"""",
                @"    FireToolExtinguishMultiplier=""" + FireToolExtinguishMultiplier + @"""",
                @"    EnginesRegenerateCondition=""" + EnginesRegenerateCondition + @"""",
                @"    EnginesRegenAmount=""" + EnginesRegenAmount + @"""",
                @"    ElectricalRegenerateCondition=""" + ElectricalRegenerateCondition + @"""",
                @"    ElectricalRegenAmount=""" + ElectricalRegenAmount + @"""",
                @"    ElectricalOverloadDamage=""" + ElectricalOverloadDamage + @"""",
                @"    ElectricalOverloadMinPower=""" + ElectricalOverloadMinPower + @"""",
                @"    ElectricalOverloadVoltRangeMin=""" + ElectricalOverloadVoltRangeMin + @"""",
                @"    ElectricalOverloadVoltRangeMax=""" + ElectricalOverloadVoltRangeMax + @"""",
                @"    ElectricalOverloadFiresChance=""" + ElectricalOverloadFiresChance + @"""",
                @"    ElectricalFailMaxVoltage=""" + ElectricalFailMaxVoltage + @"""",
                @"    ElectricalFailStunTime=""" + ElectricalFailStunTime + @"""",
                "  />",

                "",

                "  <!--These Settings are shared between the AI creatures/humans and the remote players if ever applicable-->",
                "  <ServerModAllCharacterSettings",
                @"    PlayerOxygenUsageAmount=""" + PlayerOxygenUsageAmount + @"""",
                @"    PlayerOxygenGainSpeed=""" + PlayerOxygenGainSpeed + @"""",
                @"    UseProgressiveImplodeDeath=""" + UseProgressiveImplodeDeath + @"""",
                @"    ImplodeHealthLoss=""" + ImplodeHealthLoss + @"""",
                @"    ImplodeBleedGain=""" + ImplodeBleedGain + @"""",
                @"    ImplodeOxygenLoss=""" + ImplodeOxygenLoss + @"""",
                @"    PreventImplodeHealing=""" + PreventImplodeHealing + @"""",
                @"    PreventImplodeClotting=""" + PreventImplodeClotting + @"""",
                @"    PreventImplodeOxygen=""" + PreventImplodeOxygen + @"""",
                @"    CharacterImplodeDeathAtMinHealth=""" + CharacterImplodeDeathAtMinHealth + @"""",
                @"    HuskHealingMultiplierinfected=""" + HuskHealingMultiplierinfected + @"""",
                @"    HuskHealingMultiplierincurable=""" + HuskHealingMultiplierincurable + @"""",
                @"    PlayerHuskInfectedDrain=""" + PlayerHuskInfectedDrain + @"""",
                @"    PlayerHuskIncurableDrain=""" + PlayerHuskIncurableDrain + @"""",
                @"    HealthUnconciousDecayHealth=""" + HealthUnconciousDecayHealth + @"""",
                @"    HealthUnconciousDecayBleed=""" + HealthUnconciousDecayBleed + @"""",
                @"    HealthUnconciousDecayOxygen=""" + HealthUnconciousDecayOxygen + @"""",
                @"    OxygenUnconciousDecayHealth=""" + OxygenUnconciousDecayHealth + @"""",
                @"    OxygenUnconciousDecayBleed=""" + OxygenUnconciousDecayBleed + @"""",
                @"    OxygenUnconciousDecayOxygen=""" + OxygenUnconciousDecayOxygen + @"""",
                @"    MinHealthBleedCap=""" + MinHealthBleedCap + @"""",
                @"    CreatureBleedMultiplier=""" + CreatureBleedMultiplier + @"""",
                @"    ArmourBleedBypassNoDamage=""" + ArmourBleedBypassNoDamage + @"""",
                @"    ArmourSoakFireDamage=""" + ArmourSoakFireDamage + @"""",
                @"    ArmourAbsorptionHealth=""" + ArmourAbsorptionHealth + @"""",
                @"    ArmourDirectReductionHealth=""" + ArmourDirectReductionHealth + @"""",
                @"    ArmourResistanceMultiplierHealth=""" + ArmourResistanceMultiplierHealth + @"""",
                @"    ArmourResistancePowerHealth=""" + ArmourResistancePowerHealth + @"""",
                @"    ArmourMinimumHealthPercent=""" + ArmourMinimumHealthPercent + @"""",
                @"    ArmourAbsorptionBleed=""" + ArmourAbsorptionBleed + @"""",
                @"    ArmourDirectReductionBleed=""" + ArmourDirectReductionBleed + @"""",
                @"    ArmourResistanceMultiplierBleed=""" + ArmourResistanceMultiplierBleed + @"""",
                @"    ArmourResistancePowerBleed=""" + ArmourResistancePowerBleed + @"""",
                @"    ArmourMinimumBleedPercent=""" + ArmourMinimumBleedPercent + @"""",
                "  />",

                "",

                "  <!--These Settings effect player-controlled characters (Remote player clients) and the locally controlled character-->",
                "  <ServerModPlayerSettings",
                @"    PlayerCanTraumaDeath=""" + PlayerCanTraumaDeath + @"""",
                @"    PlayerCanImplodeDeath=""" + PlayerCanImplodeDeath + @"""",
                @"    PlayerCanSuffocateDeath=""" + PlayerCanSuffocateDeath + @"""",
                @"    PlayerHealthMultiplier=""" + PlayerHealthMultiplier + @"""",
                @"    PlayerHuskHealthMultiplier=""" + PlayerHuskHealthMultiplier + @"""",
                @"    PlayerHuskAiOnDeath=""" + PlayerHuskAiOnDeath + @"""",
                @"    PlayerHealthRegen=""" + PlayerHealthRegen + @"""",
                @"    PlayerHealthRegenMin=""" + PlayerHealthRegenMin * 100 + @"""",
                @"    PlayerHealthRegenMax=""" + PlayerHealthRegenMax * 100 + @"""",
                @"    PlayerCPROnlyWhileUnconcious=""" + PlayerCPROnlyWhileUnconcious + @"""",
                @"    PlayerCPRHealthBaseValue=""" + PlayerCPRHealthBaseValue + @"""",
                @"    PlayerCPRHealthSkillMultiplier=""" + PlayerCPRHealthSkillMultiplier + @"""",
                @"    PlayerCPRHealthSkillNeeded=""" + PlayerCPRHealthSkillNeeded + @"""",
                @"    PlayerCPRStunBaseValue=""" + PlayerCPRStunBaseValue + @"""",
                @"    PlayerCPRStunSkillMultiplier=""" + PlayerCPRStunSkillMultiplier + @"""",
                @"    PlayerCPRStunSkillNeeded=""" + PlayerCPRStunSkillNeeded + @"""",
                @"    PlayerCPRClotBaseValue=""" + PlayerCPRClotBaseValue + @"""",
                @"    PlayerCPRClotSkillMultiplier=""" + PlayerCPRClotSkillMultiplier + @"""",
                @"    PlayerCPRClotSkillNeeded=""" + PlayerCPRClotSkillNeeded + @"""",
                @"    PlayerCPROxygenBaseValue=""" + PlayerCPROxygenBaseValue + @"""",
                @"    PlayerCPROxygenSkillMultiplier=""" + PlayerCPROxygenSkillMultiplier + @"""",
                @"    PlayerUnconciousTimer=""" + PlayerUnconciousTimer + @"""",
                "  />",

                "",

                "  <ServerModHostSettings",
                @"    PlayYourselfName=""" + PlayYourselfName + @"""",
                @"    HostBypassSkills=""" + HostBypassSkills + @"""",
                "  />",

                "",

                "  <!--These Settings effect AI-Controlled creatures (But not Player-Controlled ones, those use PlayerSettings instead)-->",
                "  <ServerModAICreatureSettings",
                @"    CreatureHealthMultiplier=""" + CreatureHealthMultiplier + @"""",
                @"    CreatureHealthRegen=""" + CreatureHealthRegen + @"""",
                @"    CreatureHealthRegenMin=""" + CreatureHealthRegenMin * 100 + @"""",
                @"    CreatureHealthRegenMax=""" + CreatureHealthRegenMax * 100 + @"""",
                @"    CreatureEatDyingPlayers=""" + CreatureEatDyingPlayers + @"""",
                @"    CreatureRespawnMonsterEvents=""" + CreatureRespawnMonsterEvents + @"""",
                @"    CreatureLimitRespawns=""" + CreatureLimitRespawns + @"""",
                @"    CreatureMaxRespawns=""" + CreatureMaxRespawns + @"""",
                "  />",

                "",

                //"<!--Which mission types can actually be selected Via 'random' by this server-->",
                //"  <NilModRandomMission",
                //@"    Cargo=""true""",
                //@"    Salvage=""true""",
                //@"    Monster=""true""",
                //@"    Combat=""true""",
                //"  />",

                //"",
                
                //"",
                //"  <!--If started with -startserver OR StartToServer above is true, Use the following Monster Spawns settings-->",
                //"  <NilModDefaultServerSpawns",
                //@"    Carrier=""true""",
                //@"    Charybdis=""true""",
                //@"    Coelanth=""true""",
                //@"    Crawler=""true""",
                //@"    Endworm=""true""",
                //@"    Fractalguardian=""true""",
                //@"    Fractalguardian2=""true""",
                //@"    Human=""true""",
                //@"    Husk=""true""",
                //@"    Mantis=""true""",
                //@"    Moloch=""true""",
                //@"    Scorpion=""true""",
                //@"    Tigerthresher=""true""",
                //@"    Watcher=""true""",
                //"  />",
                //"",
                //"  <!--If started with -startserver OR StartToServer above is true, Use the following Additional Cargo Settings-->",
                //"  <NilModAdditionalCargo>",
                //@"    <Item name=""Oxygenite Shard"" Quantiy=""0""/>",
                //"  </NilModAdditionalCargo>",
                "</NilMod>"
            };
            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(SettingsSavePath, false, Encoding.UTF8))
            {
                foreach (string line in lines)
                {
                    file.WriteLine(line);
                }
            }
        }

        public void ResetToDefault()
        {
            //Core Settings
            BypassMD5 = false;
            ServerMD5A = GameMain.SelectedPackage.MD5hash.Hash;
            ServerMD5B = "";
            DebugConsoleTimeStamp = false;
            MaxLogMessages = 800;
            LogAppendCurrentRound = false;
            LogAppendLineSaveRate = 5;
            ClearLogRoundStart = false;
            ChatboxHeight = 0.15f;
            ChatboxWidth = 0.35f;
            ChatboxMaxMessages = 20;
            ShowRoomInfo = false;
            UseUpdatedCharHUD = false;
            UseCreatureZoomBoost = false;
            CreatureZoomMultiplier = 1f;
            StartToServer = false;
            EnableEventChatterSystem = false;
            EnableHelpSystem = false;
            EnableAdminSystem = false;
            EnablePlayerLogSystem = false;
            NilMod.NilModPlayerLog.PlayerLogStateNames = false;
            NilMod.NilModPlayerLog.PlayerLogStateFirstJoinedNames = false;
            NilMod.NilModPlayerLog.PlayerLogStateLastJoinedNames = false;
            EnableVPNBanlist = false;
            SubVotingConsoleLog = false;
            SubVotingServerLog = false;
            SubVotingAnnounce = false;
            LogAIDamage = false;
            CrashRestart = false;
            UseStartWindowPosition = false;
            StartXPos = 0;
            StartYPos = 0;
            BanListReloadTimer = 15f;
            BansOverrideBannedInfo = true;
            BansInfoCustomtext = "";
            BansInfoAddBanName = true;
            BansInfoAddBanDuration = true;
            BansInfoUseRemainingTime = true;
            BansInfoAddCustomString = false;
            BansInfoAddBanReason = false;
            VoteKickStateNameTimer = 600f;
            VoteKickDenyRejoinTimer = 60f;
            AdminKickStateNameTimer = 120f;
            AdminKickDenyRejoinTimer = 20f;
            KickStateNameTimerIncreaseOnRejoin = 60f;
            KickMaxStateNameTimer = 60f;
            ClearKickStateNameOnRejoin = false;
            ClearKicksOnRoundStart = false;

        //Server Default Settings
            ServerName = "Barotrauma Server";
            ServerPort = 14242;
            MaxPlayers = 8;
            UseServerPassword = false;
            ServerPassword = "";
            AdminAuth = "";
            PublicServer = true;
            UPNPForwarding = false;
            AutoRestart = false;
            DefaultGamemode = "Sandbox";
            DefaultMissionType = "Random";
            DefaultRespawnShuttle = "";
            DefaultSubmarine = "";
            DefaultLevelSeed = "";
            SetDefaultsAlways = false;
            UseAlternativeNetworking = false;
            UseCharStatOptimisation = true;
            CharacterDisabledistance = 20000.0f;
            NetConfig.CharacterIgnoreDistance = CharacterDisabledistance;
            NetConfig.CharacterIgnoreDistanceSqr = CharacterDisabledistance * CharacterDisabledistance;
            ItemPosUpdateDistance = 2.00f;
            NetConfig.ItemPosUpdateDistance = ItemPosUpdateDistance;
            DesyncTimerMultiplier = 1.00f;
            DisableParticlesOnStart = false;
            DisableLightsOnStart = false;
            DisableLOSOnStart = false;
            AllowReconnect = false;
            ReconnectAddStun = 5f;
            ReconnectTimeAllowed = 30f;

            //Debug Settings
            DebugReportSettingsOnLoad = false;
            ShowPacketMTUErrors = true;
            ShowOpenALErrors = true;
            ShowPathfindingErrors = true;
            ShowMasterServerSuccess = true;
            DebugLag = false;
            DebugLagSimulatedPacketLoss = 0.05f;
            DebugLagSimulatedRandomLatency = 0.05f;
            DebugLagSimulatedDuplicatesChance = 0.05f;
            DebugLagSimulatedMinimumLatency = 0.10f;
            DebugLagConnectionTimeout = 60f;
            MaxParticles = 1500;
            ParticleSpawnPercent = 100;
            ParticleLifeMultiplier = 1.0f;
            ParticleWhitelist = new List<string>();
            //ParticleWhitelist.Add("watersplash");
            //ParticleWhitelist.Add("mist");
            //ParticleWhitelist.Add("dustcloud");
            //ParticleWhitelist.Add("bubbles");
            ParticleWhitelist.Add("blood");
            ParticleWhitelist.Add("waterblood");
            ParticleWhitelist.Add("spark");
            ParticleWhitelist.Add("shockwave");
            ParticleWhitelist.Add("flame");
            ParticleWhitelist.Add("steam");
            ParticleWhitelist.Add("smoke");
            ParticleWhitelist.Add("explosionfire");
            ParticleWhitelist.Add("weld");
            ParticleWhitelist.Add("plasma");
            ParticleWhitelist.Add("largeplasma");
            ParticleWhitelist.Add("extinguisher");
            //ParticleWhitelist.Add("flare");
            //ParticleWhitelist.Add("shrapnel");
            //ParticleWhitelist.Add("iceshards");

            UseDesyncPrevention = false;
            DesyncPreventionItemPassTimer = 0.15f;
            DesyncPreventionPassItemCount = 20;
            DesyncPreventionPlayerStatusTimer = 0.5f;
            DesyncPreventionPassPlayerStatusCount = 3;
            DesyncPreventionHullStatusTimer = 1f;
            DesyncPreventionPassHullCount = 1;

            //Respawn Settings
            LimitCharacterRespawns = false;
            LimitShuttleRespawns = false;
            MaxRespawnCharacters = -1;
            MaxRespawnShuttles = -1;
            BaseRespawnCharacters = 0f;
            BaseRespawnShuttles = 0f;
            RespawnCharactersPerPlayer = 0f;
            RespawnShuttlesPerPlayer = 0f;
            AlwaysRespawnNewConnections = false;
            RespawnNewConnectionsToSub = false;
            RespawnOnMainSub = false;
            RespawnWearingSuitGear = false;
            RespawnLeavingAutoPilotMode = 0;
            RespawnShuttleLeavingCloseDoors = true;
            RespawnShuttleLeavingUndock = true;
            RespawnShuttleLeaveAtTime = -1f;

            //Submarine Settings
            HullOxygenDistributionSpeed = 500f;
            HullOxygenDetoriationSpeed = 0.3f;
            HullOxygenConsumptionSpeed = 1000f;
            HullUnbreathablePercent = 30.0f;
            CanDamageSubBody = true;
            CanRewireMainSubs = true;
            CrushDamageDepth = -30000f;
            PlayerCrushDepthInHull = -30000f;
            PlayerCrushDepthOutsideHull = -30000f;
            UseProgressiveCrush = false;
            PCrushUseWallRemainingHealthCheck = false;
            PCrushDepthHealthResistMultiplier = 1.0f;
            PCrushDepthBaseHealthResist = 0f;
            PCrushDamageDepthMultiplier = 1.0f;
            PCrushBaseDamage = 0f;
            PCrushWallHealthDamagePercent = 0f;
            PCrushWallBaseDamageChance = 35f;
            PCrushWallDamageChanceIncrease = 5f;
            PCrushWallMaxDamageChance = 100f;
            PCrushInterval = 10f;
            SyncFireSizeChange = false;
            FireSyncFrequency = 4f;
            FireSizeChangeToSync = 6f;
            FireProofDamagePercentReduction = 100f;
            FireProofRangePercentReduction = 0f;
            FireCharDamageMultiplier = 1f;
            FireCharRangeMultiplier = 1f;
            FireItemRangeMultiplier = 1f;
            FireUseRangedDamage = false;
            FireRangedDamageStrength = 1f;
            FireRangedDamageMinMultiplier = 0.05f;
            FireRangedDamageMaxMultiplier = 1f;
            FireOxygenConsumption = 50f;
            FireGrowthSpeed = 5f;
            FireShrinkSpeed = 5f;
            FireWaterExtinguishMultiplier = 1f;
            FireToolExtinguishMultiplier = 1f;
            EnginesRegenerateCondition = false;
            EnginesRegenAmount = 0f;
            ElectricalRegenerateCondition = false;
            ElectricalRegenAmount = 0f;
            ElectricalOverloadDamage = 10f;
            ElectricalOverloadMinPower = 200f;
            ElectricalOverloadVoltRangeMin = 1.9f;
            ElectricalOverloadVoltRangeMax = 2.1f;
            ElectricalOverloadFiresChance = 15f;
            ElectricalFailMaxVoltage = 0.1f;
            ElectricalFailStunTime = 5f;

            //All Character Settings
            PlayerOxygenUsageAmount = -5.0f;
            PlayerOxygenGainSpeed = 10.0f;
            UseProgressiveImplodeDeath = false;
            ImplodeHealthLoss = 0.35f;
            ImplodeBleedGain = 0.12f;
            ImplodeOxygenLoss = 1.5f;
            PreventImplodeHealing = true;
            PreventImplodeClotting = true;
            PreventImplodeOxygen = true;
            CharacterImplodeDeathAtMinHealth = true;
            HuskHealingMultiplierinfected = 1.0f;
            HuskHealingMultiplierincurable = 1.0f;
            PlayerHuskInfectedDrain = 0.0f;
            PlayerHuskIncurableDrain = 0.50f;
            HealthUnconciousDecayHealth = 0.5f;
            HealthUnconciousDecayBleed = 0.0f;
            HealthUnconciousDecayOxygen = 0.0f;
            OxygenUnconciousDecayHealth = 0.0f;
            OxygenUnconciousDecayBleed = 0.0f;
            OxygenUnconciousDecayOxygen = 0.0f;
            MinHealthBleedCap = 5f;
            CreatureBleedMultiplier = 1.00f;
            ArmourBleedBypassNoDamage = false;
            ArmourAbsorptionHealth = 0f;
            ArmourDirectReductionHealth = 1f;
            ArmourMinimumHealthPercent = 0f;
            ArmourResistancePowerHealth = 0f;
            ArmourResistanceMultiplierHealth = 0f;
            ArmourAbsorptionBleed = 0f;
            ArmourDirectReductionBleed = 1f;
            ArmourResistancePowerBleed = 0f;
            ArmourResistanceMultiplierBleed = 0f;
            ArmourMinimumBleedPercent = 0f;




            //Player Settings
            PlayerCanTraumaDeath = true;
            PlayerCanImplodeDeath = true;
            PlayerCanSuffocateDeath = true;
            PlayerHealthMultiplier = 1f;
            PlayerHuskHealthMultiplier = 1f;
            PlayerHuskAiOnDeath = true;
            PlayerHealthRegen = 0f;
            PlayerHealthRegenMin = -100f;
            PlayerHealthRegenMax = 100f;
            PlayerCPROnlyWhileUnconcious = true;
            PlayerCPRHealthBaseValue = 0f;
            PlayerCPRHealthSkillMultiplier = 0f;
            PlayerCPRHealthSkillNeeded = 100;
            PlayerCPRStunBaseValue = 0f;
            PlayerCPRStunSkillMultiplier = 0f;
            PlayerCPRStunSkillNeeded = 0;
            PlayerCPRClotBaseValue = 0f;
            PlayerCPRClotSkillMultiplier = 0f;
            PlayerCPRClotSkillNeeded = 0;
            PlayerCPROxygenBaseValue = 0f;
            PlayerCPROxygenSkillMultiplier = 0.1f;
            PlayerUnconciousTimer = 5f;

            //Host Specific settings
            PlayYourselfName = "";
            HostBypassSkills = false;

            //Creature specific settings
            CreatureHealthMultiplier = 1f;
            CreatureHealthRegen = 0f;
            CreatureHealthRegenMin = 0f;
            CreatureHealthRegenMax = 100f;
            CreatureEatDyingPlayers = true;
            CreatureRespawnMonsterEvents = true;

            //Default Server Creatures to spawn

            //Additional Cargo Spawning defaults

            //Just Disable everything in the class despite whatever it loaded. simples
                NilMod.NilModEventChatter.ChatCargo = false;
                NilMod.NilModEventChatter.ChatModServerJoin = false;
                NilMod.NilModEventChatter.ChatMonster = false;
                NilMod.NilModEventChatter.ChatNoneTraitorReminder = false;
                NilMod.NilModEventChatter.ChatSalvage = false;
                NilMod.NilModEventChatter.ChatSandbox = false;
                NilMod.NilModEventChatter.ChatShuttleLeavingKill = false;
                NilMod.NilModEventChatter.ChatShuttleRespawn = false;
                NilMod.NilModEventChatter.ChatSubvsSub = false;
                NilMod.NilModEventChatter.ChatTraitorReminder = false;
                NilMod.NilModEventChatter.ChatVoteEnd = false;
        }

        public void LoadComponants()
        {
            //Load the other components
            NilModHelpCommands.Load();
            NilModEventChatter.Load();
            NilModPlayerLog.Load();
            //NilModPermissions.Load();
        }

        public void TestArmour(float armourrating)
        {
            DebugConsole.NewMessage("ArmourDebug Test Calculations (Does not factor bleed bypass or impossible reverse damage):", Color.White);
            //1 armour damage reduction console messages
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 001, Bleed: 00.25 Damage Taken: "
                + Limb.CalculateHealthArmor(1f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(0.25f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 003, Bleed: 00.50 Damage Taken: "
                + Limb.CalculateHealthArmor(3f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(0.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 005, Bleed: 00.75 Damage Taken: "
                + Limb.CalculateHealthArmor(5f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(0.75f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 010, Bleed: 01.00 Damage Taken: "
                + Limb.CalculateHealthArmor(10f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(1f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 015, Bleed: 01.25 Damage Taken: "
                + Limb.CalculateHealthArmor(15f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(1.25f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 020, Bleed: 01.50 Damage Taken: "
                + Limb.CalculateHealthArmor(20f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(1.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 025, Bleed: 01.75 Damage Taken: "
                + Limb.CalculateHealthArmor(25f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(1.75f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 030, Bleed: 02.00 Damage Taken: "
                + Limb.CalculateHealthArmor(30f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(2f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 035, Bleed: 02.25 Damage Taken: "
                + Limb.CalculateHealthArmor(35f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(2.25f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 040, Bleed: 02.50 Damage Taken: "
                + Limb.CalculateHealthArmor(40f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(2.50f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 045, Bleed: 03.00 Damage Taken: "
                + Limb.CalculateHealthArmor(45f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(3f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 050, Bleed: 03.50 Damage Taken: "
                + Limb.CalculateHealthArmor(50f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(3.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 060, Bleed: 04.00 Damage Taken: "
                + Limb.CalculateHealthArmor(60f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(4f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 070, Bleed: 05.00 Damage Taken: "
                + Limb.CalculateHealthArmor(70f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 080, Bleed: 06.00 Damage Taken: "
                + Limb.CalculateHealthArmor(80f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(6f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 090, Bleed: 07.50 Damage Taken: "
                + Limb.CalculateHealthArmor(90f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(7.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 100, Bleed: 10.00 Damage Taken: "
                + Limb.CalculateHealthArmor(100f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(10f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 125, Bleed: 12.50 Damage Taken: "
                + Limb.CalculateHealthArmor(125f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(12.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 150, Bleed: 15.00 Damage Taken: "
                + Limb.CalculateHealthArmor(150f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(15f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 175, Bleed: 17.50 Damage Taken: "
                + Limb.CalculateHealthArmor(175f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(17.5f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 200, Bleed: 20.00 Damage Taken: "
                + Limb.CalculateHealthArmor(200f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(20f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 250, Bleed: 25.00 Damage Taken: "
                + Limb.CalculateHealthArmor(250f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(25f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 300, Bleed: 30.00 Damage Taken: "
                + Limb.CalculateHealthArmor(300f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(30f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 350, Bleed: 40.00 Damage Taken: "
                + Limb.CalculateHealthArmor(350f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(40f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 400, Bleed: 50.00 Damage Taken: "
                + Limb.CalculateHealthArmor(400f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(50f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 500, Bleed: 60.00 Damage Taken: "
                + Limb.CalculateHealthArmor(500f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(60f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 600, Bleed: 75.00 Damage Taken: "
                + Limb.CalculateHealthArmor(600f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(75f, armourrating), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 800, Bleed: 90.00 Damage Taken: "
                + Limb.CalculateHealthArmor(800f, armourrating) + " Bleed Taken: " + Limb.CalculateBleedArmor(90f, armourrating), Color.White);
        }

        //Things that are generally done at round start or server start for the server.
        //Note, need to move more code here to clean up project someday (Such as mission selection preferably).
        public void ServerInitialize(Boolean InitialLaunch)
        {
            if (InitialLaunch)
            {
                UseCharStatOptimisation = true;
                GameMain.Server.AutoRestart = AutoRestart;

                if (DisableParticlesOnStart) DisableParticles = true;
            }

#if CLIENT
            if (DisableLightsOnStart) GameMain.LightManager.LightingEnabled = false;

            if (DisableLOSOnStart) GameMain.LightManager.LosEnabled = false;
#endif

            if (convertinghusklist != null)
            {
                convertinghusklist.Clear();
            }
            else
            {
                convertinghusklist = new List<ConvertingHusk>();
            }

            if (DisconnectedCharacters != null)
            {
                DisconnectedCharacters.Clear();
            }
            else
            {
                DisconnectedCharacters = new List<DisconnectedCharacter>();
            }

            if(KickedClients != null)
            {
                if(ClearKicksOnRoundStart) KickedClients.Clear();
            }
            else
            {
                KickedClients = new List<KickedClient>();
            }

            if (FrozenCharacters != null)
            {
                FrozenCharacters.Clear();
            }
            else
            {
                FrozenCharacters = new List<Character>();
            }

            if (ModifiedCharacterValues != null)
            {
                ModifiedCharacterValues.Clear();
            }
            else
            {
                ModifiedCharacterValues = new List<ModifiedCharacterStat>();
            }

            DesyncPreventionItemList = new List<Item>();
            DesyncPreventionPlayerStatusList = new List<Character>();
            DesyncPreventionHullList = new List<Hull>();
            DesyncPreventionItemPassTimerleft = 30f + DesyncPreventionItemPassTimer;
            DesyncPreventionPlayerStatusTimerleft = 30f + DesyncPreventionPlayerStatusTimer;
            DesyncPreventionHullStatusTimerleft = 30f + DesyncPreventionHullStatusTimerleft;
        }
    }
}
