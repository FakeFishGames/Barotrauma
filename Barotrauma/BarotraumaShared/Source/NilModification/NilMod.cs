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
    class ConvertingHusk
    {
        public Character character;
        public int Updatestildisable;
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
        public const string NilModVersionDate = "11/02/2017 - 1";
        public Version NilModNetworkingVersion = new Version(0,0,0,0);

        public int Admins;
        public int Moderators;
        public int Spectators;

        public string ExternalIP = "?.?.?.?";
        System.Net.WebClient ExternalIPWebClient;

        public Boolean SetToDefault = false;
        public Boolean Skippedtoserver = false;
        public String LoadingText = "Loading to Main Menu";

        //Nilmod Client-Server Syncing code.
        public Boolean ClientWriteNilModSyncResponse = false;
        public Boolean NilModSynced = false;
        public float SyncResendTimer = 0f;
        public const float SyncResendInterval = 1f;

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
        public int HostTeamPreference = 0;

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

        public int MaxAdminSlots;
        public int MaxModeratorSlots;
        public int MaxSpectatorSlots;

        public Boolean DebugConsoleTimeStamp;

        public int MaxLogMessages;
        public Boolean ClearLogRoundStart;
        public Boolean LogAppendCurrentRound;
        public int LogAppendLineSaveRate;

        public float ChatboxWidth;
        public float ChatboxHeight;
        public int ChatboxMaxMessages;

        public Boolean RebalanceTeamPreferences;

        public Boolean ShowRoomInfo;
        public Boolean UseUpdatedCharHUD;
        public Boolean UseRecolouredNameInfo;
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

        //Server Settings
        public Boolean OverrideGamesettings;
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
        public string CampaignSaveName;
        public Boolean SetDefaultsAlways;
        public Boolean UseAlternativeNetworking;
        public float CharacterDisabledistance;
        public float ItemPosUpdateDistance;
        public float DesyncTimerMultiplier;
        public Boolean LogAIDamage;
        public Boolean LogStatusEffectStun;
        public Boolean LogStatusEffectHealth;
        public Boolean LogStatusEffectBleed;
        public Boolean LogStatusEffectOxygen;
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
        public Color WaterColour;
        public float HullOxygenDistributionSpeed;
        public float HullOxygenDetoriationSpeed;
        public float HullOxygenConsumptionSpeed;
        public float HullUnbreathablePercent;
        public Boolean SyncFireSizeChange;
        public float FireSyncFrequency;
        public float FireSizeChangeToSync;
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

        //Client Setup
        public Boolean AllowNilModClients;
        public Boolean AllowVanillaClients;
        Boolean cl_UseUpdatedCharHUD;
        Boolean cl_UseCreatureZoomBoost;
        float cl_CreatureZoomMultiplier;
        Boolean cl_UseRecolouredNameInfo;

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
                    if (UseDesyncPrevention)
                    {
                        //Item position Anti Desync (Where items are on the server)
                        if (DesyncPreventionItemPassTimerleft <= 0f)
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
                        if (DesyncPreventionPlayerStatusTimerleft <= 0f)
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
                                        if (GameMain.Server.ConnectedClients[i].Character != null && !GameMain.Server.ConnectedClients[i].Character.IsDead && GameMain.Server.ConnectedClients[i].InGame)
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
                        if (DesyncPreventionHullStatusTimerleft <= 0f)
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

                                Client clientmatch = GameMain.Server?.ConnectedClients?.Find(c => c.Name == DisconnectedCharacters[i].clientname && c.Connection.RemoteEndPoint.Address.ToString() == DisconnectedCharacters[i].IPAddress);

                                if (clientmatch != null && DisconnectedCharacters[i].ClientSetCooldown <= 0f && clientmatch.InGame)
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
                        if (KickedClients[i].RejoinTimer > 0f)
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

                //Each client gets their OWN resync timer to prevent network spikes (This way the packets can be spread across multiple updates, incase they get chunky).
                if(GameMain.Server.ConnectedClients.Count > 0)
                {
                    for(int i = GameMain.Server.ConnectedClients.Count - 1;i >= 0;i--)
                    {
                        if(GameMain.Server.ConnectedClients[i].NilModSyncResendTimer >= 0f) GameMain.Server.ConnectedClients[i].NilModSyncResendTimer -= deltaTime;
                    }
                }
            }

#if CLIENT
            if (GameMain.GameSession != null && GameSession.inGameInfo != null) GameSession.inGameInfo.Update(deltaTime);
#endif

            //Cycle the outline flash colours
            CharFlashColourTime += deltaTime;
            if (CharFlashColourTime >= CharFlashColourRate) CharFlashColourTime = 0f;

            //Countdown to disable temporarilly removed characters

            if (convertinghusklist.Count() > 0)
            {
                for (int i = convertinghusklist.Count() - 1; i > 0; i--)
                {
                    if (convertinghusklist[i].Updatestildisable > 0)
                    {
                        convertinghusklist[i].Updatestildisable -= 1;
                        if (convertinghusklist[i].Updatestildisable <= 0)
                        {
                            Entity.Spawner.AddToRemoveQueue(convertinghusklist[i].character);
                            convertinghusklist[i].character.Enabled = false;
                            //convertinghusklist.RemoveAt(i);
                        }
                    }
                }
            }

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
            GameMain.Server.ServerLog.WriteLine("MaxAdminSlots = " + MaxAdminSlots.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxSpectatorSlots = " + MaxSpectatorSlots.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("DebugConsoleTimeStamp = " + DebugConsoleTimeStamp.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("MaxLogMessages = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ClearLogRoundStart = " + (ClearLogRoundStart ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogAppendCurrentRound = " + (LogAppendCurrentRound ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogAppendLineSaveRate = " + LogAppendLineSaveRate.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ClearLogRoundStart = " + (ClearLogRoundStart ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxHeight = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxWidth = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("ChatboxMaxMessages = " + MaxLogMessages.ToString(), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("RebalanceTeamPreferences = " + (RebalanceTeamPreferences ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
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
            GameMain.Server.ServerLog.WriteLine("LogStatusEffectStun = " + (LogStatusEffectStun ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogStatusEffectHealth = " + (LogStatusEffectHealth ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogStatusEffectBleed = " + (LogStatusEffectBleed ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
            GameMain.Server.ServerLog.WriteLine("LogStatusEffectOxygen = " + (LogStatusEffectOxygen ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);

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
            GameMain.Server.ServerLog.WriteLine("CampaignSaveName = " + CampaignSaveName.ToString(), ServerLog.MessageType.NilMod);
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
            GameMain.Server.ServerLog.WriteLine("CanRewireMainSubs = " + (CanRewireMainSubs ? "Enabled" : "Disabled"), ServerLog.MessageType.NilMod);
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

        public void Load(Boolean loadVanilla = false)
        {
            //ServerModSetupDefaultServer = false;

            NilModHelpCommands = new NilModHelpCommands();
            NilModEventChatter = new NilModEventChatter();
            NilModPermissions = new NilModPermissions();
            NilModPlayerLog = new PlayerLog();

            if (!loadVanilla)
            {

                XDocument doc = null;

                if (File.Exists(SettingsSavePath))
                {
                    ResetToDefault();
                    doc = XMLExtensions.TryLoadXml(SettingsSavePath);
                }
                //We have not actually started once yet, lets reset to current versions default instead without errors.
                else
                {
                    ResetToDefault();
                    Save();
                    doc = XMLExtensions.TryLoadXml(SettingsSavePath);
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

                    BypassMD5 = ServerModGeneralSettings.GetAttributeBool("BypassMD5", false); //Implemented
                    ServerMD5A = ServerModGeneralSettings.GetAttributeString("ServerMD5A", GameMain.SelectedPackage.MD5hash.Hash); //Implemented
                    ServerMD5B = ServerModGeneralSettings.GetAttributeString("ServerMD5B", GameMain.SelectedPackage.MD5hash.Hash); //Implemented
                    MaxAdminSlots = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("MaxAdminSlots", 0), 0, 16);
                    MaxModeratorSlots = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("MaxModeratorSlots", 0), 0, 16);
                    MaxSpectatorSlots = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("MaxSpectatorSlots", 0), 0, 16);
                    DebugConsoleTimeStamp = ServerModGeneralSettings.GetAttributeBool("DebugConsoleTimeStamp", false);
                    MaxLogMessages = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("MaxLogMessages", 800), 10, 16000); //Implemented
                    LogAppendCurrentRound = ServerModGeneralSettings.GetAttributeBool("LogAppendCurrentRound", false); //Implemented
                    LogAppendLineSaveRate = Math.Min(MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("LogAppendLineSaveRate", 5), 1, 16000), MaxLogMessages); //Implemented
                    ClearLogRoundStart = ServerModGeneralSettings.GetAttributeBool("ClearLogRoundStart", false); //Implemented
                    ChatboxHeight = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("ChatboxHeight", 0.15f), 0.10f, 0.50f);
                    ChatboxWidth = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("ChatboxWidth", 0.35f), 0.25f, 0.85f);
                    ChatboxMaxMessages = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("ChatboxMaxMessages", 20), 10, 100);
                    RebalanceTeamPreferences = ServerModGeneralSettings.GetAttributeBool("RebalanceTeamPreferences", true);
                    ShowRoomInfo = ServerModGeneralSettings.GetAttributeBool("ShowRoomInfo", false);
                    UseUpdatedCharHUD = ServerModGeneralSettings.GetAttributeBool("UseUpdatedCharHUD", false);
                    UseRecolouredNameInfo = ServerModGeneralSettings.GetAttributeBool("UseRecolouredNameInfo", false);
                    UseCreatureZoomBoost = ServerModGeneralSettings.GetAttributeBool("UseCreatureZoomBoost", false);
                    CreatureZoomMultiplier = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("CreatureZoomMultiplier", 1f), 0.4f, 3f);
                    StartToServer = ServerModGeneralSettings.GetAttributeBool("StartToServer", false); //Implemented
                    EnableEventChatterSystem = ServerModGeneralSettings.GetAttributeBool("EnableEventChatterSystem", false);
                    EnableHelpSystem = ServerModGeneralSettings.GetAttributeBool("EnableHelpSystem", false);
                    EnableAdminSystem = ServerModGeneralSettings.GetAttributeBool("EnableAdminSystem", false);
                    EnablePlayerLogSystem = ServerModGeneralSettings.GetAttributeBool("EnablePlayerLogSystem", false);
                    NilMod.NilModPlayerLog.PlayerLogStateNames = ServerModGeneralSettings.GetAttributeBool("PlayerLogStateNames", false);
                    NilMod.NilModPlayerLog.PlayerLogStateFirstJoinedNames = ServerModGeneralSettings.GetAttributeBool("PlayerLogStateFirstJoinedNames", false);
                    NilMod.NilModPlayerLog.PlayerLogStateLastJoinedNames = ServerModGeneralSettings.GetAttributeBool("PlayerLogStateLastJoinedNames", false);
                    EnableVPNBanlist = ServerModGeneralSettings.GetAttributeBool("EnableVPNBanlist", false);
                    SubVotingConsoleLog = ServerModGeneralSettings.GetAttributeBool("SubVotingConsoleLog", false);
                    SubVotingServerLog = ServerModGeneralSettings.GetAttributeBool("SubVotingServerLog", false);
                    SubVotingAnnounce = ServerModGeneralSettings.GetAttributeBool("SubVotingAnnounce", false);
                    LogAIDamage = ServerModGeneralSettings.GetAttributeBool("LogAIDamage", false);
                    LogStatusEffectStun = ServerModGeneralSettings.GetAttributeBool("LogStatusEffectStun", false);
                    LogStatusEffectHealth = ServerModGeneralSettings.GetAttributeBool("LogStatusEffectHealth", false);
                    LogStatusEffectBleed = ServerModGeneralSettings.GetAttributeBool("LogStatusEffectBleed", false);
                    LogStatusEffectOxygen = ServerModGeneralSettings.GetAttributeBool("LogStatusEffectOxygen", false);
                    CrashRestart = ServerModGeneralSettings.GetAttributeBool("CrashRestart", false);
                    StartXPos = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("StartXPos", 0), 0, 16000);
                    StartYPos = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("StartYPos", 0), 0, 16000);
                    UseStartWindowPosition = ServerModGeneralSettings.GetAttributeBool("UseStartWindowPosition", false);
                    BanListReloadTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeInt("BanListReloadTimer", 15), 0, 60);
                    BansOverrideBannedInfo = ServerModGeneralSettings.GetAttributeBool("BansOverrideBannedInfo", true);
                    BansInfoAddBanName = ServerModGeneralSettings.GetAttributeBool("BansInfoAddBanName", true);
                    BansInfoAddBanDuration = ServerModGeneralSettings.GetAttributeBool("BansInfoAddBanDuration", true);
                    BansInfoUseRemainingTime = ServerModGeneralSettings.GetAttributeBool("BansInfoUseRemainingTime", true);
                    BansInfoAddCustomString = ServerModGeneralSettings.GetAttributeBool("BansInfoAddCustomString", false);
                    BansInfoCustomtext = ServerModGeneralSettings.GetAttributeString("BansInfoCustomtext", "");
                    BansInfoAddBanReason = ServerModGeneralSettings.GetAttributeBool("BansInfoAddBanReason", false);
                    VoteKickStateNameTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("VoteKickStateNameTimer", 600f), 0f, 86400f);
                    VoteKickDenyRejoinTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("VoteKickDenyRejoinTimer", 60f), 0f, 86400f);
                    AdminKickStateNameTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("AdminKickStateNameTimer", 120f), 0f, 86400f);
                    AdminKickDenyRejoinTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("AdminKickDenyRejoinTimer", 20f), 0f, 86400f);
                    KickStateNameTimerIncreaseOnRejoin = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("KickStateNameTimerIncreaseOnRejoin", 60f), 0f, 86400f);
                    KickMaxStateNameTimer = MathHelper.Clamp(ServerModGeneralSettings.GetAttributeFloat("KickMaxStateNameTimer", 60f), 0f, 86400f);
                    ClearKickStateNameOnRejoin = ServerModGeneralSettings.GetAttributeBool("ClearKickStateNameOnRejoin", false);
                    ClearKicksOnRoundStart = ServerModGeneralSettings.GetAttributeBool("ClearKicksOnRoundStart", false);

                    //Server Default Settings
                    XElement ServerModDefaultServerSettings = doc.Root.Element("ServerModDefaultServerSettings");

                    ServerName = ServerModDefaultServerSettings.GetAttributeString("ServerName", "Barotrauma Server");
                    //Sanitize Server Name Code here
                    if (ServerName != "")
                    {
                        string newservername = Client.SanitizeName(ServerName).Trim();
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

                    ServerPort = Math.Min(Math.Max(ServerModDefaultServerSettings.GetAttributeInt("ServerPort", 14242), 1025), 65536);

                    MaxPlayers = Math.Min(Math.Max(ServerModDefaultServerSettings.GetAttributeInt("MaxPlayers", 8), 1), 32);
                    UseServerPassword = ServerModDefaultServerSettings.GetAttributeBool("UseServerPassword", false);
                    ServerPassword = ServerModDefaultServerSettings.GetAttributeString("ServerPassword", "");
                    AdminAuth = ServerModDefaultServerSettings.GetAttributeString("AdminAuth", "");
                    PublicServer = ServerModDefaultServerSettings.GetAttributeBool("PublicServer", true);
                    UPNPForwarding = ServerModDefaultServerSettings.GetAttributeBool("UPNPForwarding", false);
                    AutoRestart = ServerModDefaultServerSettings.GetAttributeBool("AutoRestart", false);
                    DefaultGamemode = ServerModDefaultServerSettings.GetAttributeString("DefaultGamemode", "Sandbox");
                    DefaultMissionType = ServerModDefaultServerSettings.GetAttributeString("DefaultMissionType", "Random");
                    DefaultRespawnShuttle = ServerModDefaultServerSettings.GetAttributeString("DefaultRespawnShuttle", "");
                    DefaultSubmarine = ServerModDefaultServerSettings.GetAttributeString("DefaultSubmarine", "");
                    DefaultLevelSeed = ServerModDefaultServerSettings.GetAttributeString("DefaultLevelSeed", "");
                    CampaignSaveName = ServerModDefaultServerSettings.GetAttributeString("CampaignSaveName", "");
                    SetDefaultsAlways = ServerModDefaultServerSettings.GetAttributeBool("SetDefaultsAlways", false);
                    UseAlternativeNetworking = ServerModDefaultServerSettings.GetAttributeBool("UseAlternativeNetworking", false);
                    CharacterDisabledistance = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("CharacterDisabledistance", 20000.0f), 10000.00f, 100000.00f);
                    NetConfig.CharacterIgnoreDistance = CharacterDisabledistance;
                    NetConfig.CharacterIgnoreDistanceSqr = CharacterDisabledistance * CharacterDisabledistance;
                    ItemPosUpdateDistance = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("ItemPosUpdateDistance", 2.00f), 0.25f, 5.00f);
                    NetConfig.ItemPosUpdateDistance = ItemPosUpdateDistance;
                    DesyncTimerMultiplier = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("DesyncTimerMultiplier", 1.00f), 1.00f, 30.00f);
                    DisableParticlesOnStart = ServerModDefaultServerSettings.GetAttributeBool("DisableParticlesOnStart", false);
                    DisableLightsOnStart = ServerModDefaultServerSettings.GetAttributeBool("DisableLightsOnStart", false);
                    DisableLOSOnStart = ServerModDefaultServerSettings.GetAttributeBool("DisableLOSOnStart", false);
                    AllowReconnect = ServerModDefaultServerSettings.GetAttributeBool("AllowReconnect", false);
                    ReconnectAddStun = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("ReconnectAddStun", 5.00f), 0.00f, 60.00f);
                    ReconnectTimeAllowed = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("ReconnectTimeAllowed", 10.00f), 10.00f, 600.00f);

                    UseDesyncPrevention = ServerModDefaultServerSettings.GetAttributeBool("UseDesyncPrevention", true);
                    DesyncPreventionItemPassTimer = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("DesyncPreventionItemPassTimer", 0.05f), 0.00f, 10.00f);
                    DesyncPreventionPassItemCount = Math.Min(Math.Max(ServerModDefaultServerSettings.GetAttributeInt("DesyncPreventionPassItemCount", 5), 0), 50);
                    DesyncPreventionPlayerStatusTimer = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("DesyncPreventionPlayerStatusTimer", 0.5f), 0.00f, 10.00f);
                    DesyncPreventionPassPlayerStatusCount = Math.Min(Math.Max(ServerModDefaultServerSettings.GetAttributeInt("DesyncPreventionPassPlayerStatusCount", 1), 0), 10);
                    DesyncPreventionHullStatusTimer = MathHelper.Clamp(ServerModDefaultServerSettings.GetAttributeFloat("DesyncPreventionHullStatusTimer", 1.5f), 0.00f, 10.00f);
                    DesyncPreventionPassHullCount = Math.Min(Math.Max(ServerModDefaultServerSettings.GetAttributeInt("DesyncPreventionPassHullCount", 1), 0), 10);

                    //Debug Settings
                    XElement ServerModDebugSettings = doc.Root.Element("ServerModDebugSettings");

                    DebugReportSettingsOnLoad = ServerModDebugSettings.GetAttributeBool("DebugReportSettingsOnLoad", false); //Implemented
                    ShowPacketMTUErrors = ServerModDebugSettings.GetAttributeBool("ShowPacketMTUErrors", true); //Implemented
                    ShowOpenALErrors = ServerModDebugSettings.GetAttributeBool("ShowOpenALErrors", true);
                    ShowPathfindingErrors = ServerModDebugSettings.GetAttributeBool("ShowPathfindingErrors", true);
                    ShowMasterServerSuccess = ServerModDebugSettings.GetAttributeBool("ShowMasterServerSuccess", true);
                    DebugLag = ServerModDebugSettings.GetAttributeBool("DebugLag", false);
                    DebugLagSimulatedPacketLoss = ServerModDebugSettings.GetAttributeFloat("DebugLagSimulatedPacketLoss", 0.05f); //Implemented
                    DebugLagSimulatedRandomLatency = ServerModDebugSettings.GetAttributeFloat("DebugLagSimulatedRandomLatency", 0.05f); //Implemented
                    DebugLagSimulatedDuplicatesChance = ServerModDebugSettings.GetAttributeFloat("DebugLagSimulatedDuplicatesChance", 0.05f); //Implemented
                    DebugLagSimulatedMinimumLatency = ServerModDebugSettings.GetAttributeFloat("DebugLagSimulatedMinimumLatency", 0.10f); //Implemented
                    DebugLagConnectionTimeout = ServerModDebugSettings.GetAttributeFloat("DebugLagConnectionTimeout", 60f); //Implemented
                    MaxParticles = Math.Min(Math.Max(ServerModDebugSettings.GetAttributeInt("MaxParticles", 1500), 150), 15000);
                    ParticleSpawnPercent = Math.Min(Math.Max(ServerModDebugSettings.GetAttributeInt("ParticleSpawnPercent", 100), 0), 100);
                    MathHelper.Clamp(ParticleLifeMultiplier = ServerModDebugSettings.GetAttributeFloat("ParticleLifeMultiplier", 1f), 0.15f, 1f); //Implemented

#if CLIENT
                    if (GameMain.ParticleManager != null)
                    {
                        GameMain.ParticleManager.ResetParticleManager();
                    }
#endif

                    //Respawn Settings
                    XElement ServerModRespawnSettings = doc.Root.Element("ServerModRespawnSettings");

                    LimitCharacterRespawns = ServerModRespawnSettings.GetAttributeBool("LimitCharacterRespawns", false); //Unimplemented
                    LimitShuttleRespawns = ServerModRespawnSettings.GetAttributeBool("LimitShuttleRespawns", false); //Unimplemented
                    MaxRespawnCharacters = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeInt("MaxRespawnCharacters", -1), -1, 10000);
                    MaxRespawnShuttles = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeInt("MaxRespawnShuttles", -1), -1, 1000);
                    BaseRespawnCharacters = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeFloat("BaseRespawnCharacters", 0f), 0f, 10000f);
                    BaseRespawnShuttles = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeFloat("BaseRespawnShuttles", 0f), 0f, 1000f);
                    RespawnCharactersPerPlayer = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeFloat("RespawnCharactersPerPlayer", 0f), 0f, 10000f);
                    RespawnShuttlesPerPlayer = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeFloat("RespawnShuttlesPerPlayer", 0f), 0f, 1000f);
                    AlwaysRespawnNewConnections = ServerModRespawnSettings.GetAttributeBool("AlwaysRespawnNewConnections", false); //Unimplemented
                    RespawnNewConnectionsToSub = ServerModRespawnSettings.GetAttributeBool("RespawnNewConnectionsToSub", false); //Unimplemented
                    RespawnOnMainSub = ServerModRespawnSettings.GetAttributeBool("RespawnOnMainSub", false); //Implemented
                    RespawnWearingSuitGear = ServerModRespawnSettings.GetAttributeBool("RespawnWearingSuitGear", false);
                    RespawnLeavingAutoPilotMode = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeInt("RespawnLeavingAutoPilotMode", 0), 0, 3);
                    RespawnShuttleLeavingCloseDoors = ServerModRespawnSettings.GetAttributeBool("RespawnShuttleLeavingCloseDoors", true);
                    RespawnShuttleLeavingUndock = ServerModRespawnSettings.GetAttributeBool("RespawnShuttleLeavingUndock", true);
                    RespawnShuttleLeaveAtTime = MathHelper.Clamp(ServerModRespawnSettings.GetAttributeFloat("RespawnShuttleLeaveAtTime", -1f), -1f, 600f);

                    //Submarine Settings
                    XElement ServerModSubmarineSettings = doc.Root.Element("ServerModSubmarineSettings");

                    Byte oceancolourR = Convert.ToByte(MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeInt("OceanColourR", 191), 0, 255));
                    Byte oceancolourG = Convert.ToByte(MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeInt("OceanColourG", 204), 0, 255));
                    Byte oceancolourB = Convert.ToByte(MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeInt("OceanColourB", 230), 0, 255));
                    Byte oceancolourA = Convert.ToByte(MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeInt("OceanColourA", 255), 0, 255));

                    WaterColour = new Color(oceancolourR, oceancolourG, oceancolourB, oceancolourA);

                    HullOxygenDistributionSpeed = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("HullOxygenDistributionSpeed", 500f), 0f, 50000f); //Implemented
                    HullOxygenDetoriationSpeed = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("HullOxygenDetoriationSpeed", 0.3f), -10000f, 50000f); //Implemented
                    HullOxygenConsumptionSpeed = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("HullOxygenConsumptionSpeed", 1000f), 0f, 50000f); //Implemented
                    HullUnbreathablePercent = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("HullUnbreathablePercent", 30.0f), 0f, 100f); //Implemented
                    CanDamageSubBody = ServerModSubmarineSettings.GetAttributeBool("CanDamageSubBody", true); //Implemented
                    CanRewireMainSubs = ServerModSubmarineSettings.GetAttributeBool("CanRewireMainSubs", true); //Not Implemented.
                    CrushDamageDepth = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("CrushDamageDepth", -30000f), -1000000f, 100000f);
                    PlayerCrushDepthInHull = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PlayerCrushDepthInHull", -30000f), -1000000f, 100000f);
                    PlayerCrushDepthOutsideHull = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PlayerCrushDepthOutsideHull", -30000f), -1000000f, 100000f);
                    UseProgressiveCrush = ServerModSubmarineSettings.GetAttributeBool("UseProgressiveCrush", false);
                    PCrushUseWallRemainingHealthCheck = ServerModSubmarineSettings.GetAttributeBool("PCrushUseWallRemainingHealthCheck", false);
                    PCrushDepthHealthResistMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushDepthHealthResistMultiplier", 1.0f), 0f, 100.0f);
                    PCrushDepthBaseHealthResist = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushDepthBaseHealthResist", 0f), 0f, 1000000f);
                    PCrushDamageDepthMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushDamageDepthMultiplier", 1.0f), 0f, 100.0f);
                    PCrushBaseDamage = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushBaseDamage", 0f), 0f, 100000000f);
                    PCrushWallHealthDamagePercent = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushWallHealthDamagePercent", 0f), 0f, 100f);
                    PCrushWallBaseDamageChance = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushWallBaseDamageChance", 35f), 0f, 100f);
                    PCrushWallDamageChanceIncrease = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushWallDamageChanceIncrease", 5f), 0f, 1000f);
                    PCrushWallMaxDamageChance = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushWallMaxDamageChance", 100f), 0f, 100f);
                    PCrushInterval = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("PCrushInterval", 10f), 0.5f, 60f);
                    SyncFireSizeChange = ServerModSubmarineSettings.GetAttributeBool("SyncFireSizeChange", false);
                    FireSyncFrequency = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireSyncFrequency", 4f), 1f, 60f);
                    FireSizeChangeToSync = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireSizeChangeToSync", 6f), 1f, 30f);
                    FireCharDamageMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireCharDamageMultiplier", 1f), 0f, 100f);
                    FireCharRangeMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireCharRangeMultiplier", 1f), 0f, 100f);
                    FireItemRangeMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireItemRangeMultiplier", 1f), 0f, 100f);
                    FireUseRangedDamage = ServerModSubmarineSettings.GetAttributeBool("FireUseRangedDamage", false);
                    FireRangedDamageStrength = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireRangedDamageStrength", 1.0f), -1f, 10f);
                    FireRangedDamageMinMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireRangedDamageMinMultiplier", 0.05f), 0f, 2f);
                    FireRangedDamageMaxMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireRangedDamageMaxMultiplier", 1f), FireRangedDamageMinMultiplier, 100f);
                    FireOxygenConsumption = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireOxygenConsumption", 50f), 0f, 50000f);
                    FireGrowthSpeed = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireGrowthSpeed", 5f), 0.1f, 1000f);
                    FireShrinkSpeed = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireShrinkSpeed", 5f), 0.1f, 1000f);
                    FireWaterExtinguishMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireWaterExtinguishMultiplier", 1f), 0.5f, 60f);
                    FireToolExtinguishMultiplier = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("FireToolExtinguishMultiplier", 1f), 0.5f, 60f);
                    EnginesRegenerateCondition = ServerModSubmarineSettings.GetAttributeBool("EnginesRegenerateCondition", false);
                    EnginesRegenAmount = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("EnginesRegenAmount", 0f), 0f, 1000f);
                    ElectricalRegenerateCondition = ServerModSubmarineSettings.GetAttributeBool("ElectricalRegenerateCondition", false);
                    ElectricalRegenAmount = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalRegenAmount", 0f), 0f, 1000f);
                    ElectricalOverloadDamage = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalOverloadDamage", 10f), 0f, 60f);
                    ElectricalOverloadMinPower = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalOverloadMinPower", 200f), 0f, 100000f);
                    ElectricalOverloadVoltRangeMin = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalOverloadVoltRangeMin", 1.9f), 0f, 60f);
                    ElectricalOverloadVoltRangeMax = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalOverloadVoltRangeMax", 2.1f), ElectricalOverloadVoltRangeMin, 60f);
                    ElectricalOverloadFiresChance = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalOverloadFiresChance", 100f), 0.5f, 60f);
                    ElectricalFailMaxVoltage = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalFailMaxVoltage", 0.1f), 0f, 100f);
                    ElectricalFailStunTime = MathHelper.Clamp(ServerModSubmarineSettings.GetAttributeFloat("ElectricalFailStunTime", 5f), 0.1f, 60f);


                    //All Character Settings
                    XElement ServerModAllCharacterSettings = doc.Root.Element("ServerModAllCharacterSettings");

                    PlayerOxygenUsageAmount = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("PlayerOxygenUsageAmount", -5.0f), -400f, 20f); //Implemented
                    PlayerOxygenGainSpeed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("PlayerOxygenGainSpeed", 10.0f), -20f, 400f); //Implemented
                    UseProgressiveImplodeDeath = ServerModAllCharacterSettings.GetAttributeBool("UseProgressiveImplodeDeath", false);
                    ImplodeHealthLoss = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ImplodeHealthLoss", 0.35f), 0f, 1000000f);
                    ImplodeBleedGain = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ImplodeBleedGain", 0.12f), 0f, 1000000f);
                    ImplodeOxygenLoss = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ImplodeOxygenLoss", 1.5f), 0f, 1000000f);
                    PreventImplodeHealing = ServerModAllCharacterSettings.GetAttributeBool("PreventImplodeHealing", false);
                    PreventImplodeClotting = ServerModAllCharacterSettings.GetAttributeBool("PreventImplodeClotting", false);
                    PreventImplodeOxygen = ServerModAllCharacterSettings.GetAttributeBool("PreventImplodeOxygen", false);
                    CharacterImplodeDeathAtMinHealth = ServerModAllCharacterSettings.GetAttributeBool("CharacterImplodeDeathAtMinHealth", true);
                    HuskHealingMultiplierinfected = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("HuskHealingMultiplierinfected", 1.0f), -1000f, 1000f);
                    HuskHealingMultiplierincurable = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("HuskHealingMultiplierincurable", 1.0f), -1000f, 1000f);
                    PlayerHuskInfectedDrain = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("PlayerHuskInfectedDrain", 0.00f), -1000f, 1000f);
                    PlayerHuskIncurableDrain = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("PlayerHuskIncurableDrain", 0.50f), -1000f, 1000f);
                    HealthUnconciousDecayHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("HealthUnconciousDecayHealth", 0.5f), -500f, 200f); //Implemented
                    HealthUnconciousDecayBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("HealthUnconciousDecayBleed", 0.0f), -500f, 200f); //Implemented
                    HealthUnconciousDecayOxygen = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("HealthUnconciousDecayOxygen", 0.0f), -100f, 200f); //Implemented
                    OxygenUnconciousDecayHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("OxygenUnconciousDecayHealth", 0.0f), -500f, 200f); //Implemented
                    OxygenUnconciousDecayBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("OxygenUnconciousDecayBleed", 0.0f), -500f, 200f); //Implemented
                    OxygenUnconciousDecayOxygen = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("OxygenUnconciousDecayOxygen", 0.0f), -100f, 200f); //Implemented
                    MinHealthBleedCap = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("MinHealthBleedCap", 5f), 0f, 5f); //Implemented
                    CreatureBleedMultiplier = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("CreatureBleedMultiplier", 1.00f), 0f, 20f);
                    ArmourBleedBypassNoDamage = ServerModAllCharacterSettings.GetAttributeBool("ArmourBleedBypassNoDamage", false);
                    ArmourAbsorptionHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourAbsorptionHealth", 0f), 1f, 10000f);
                    ArmourDirectReductionHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourDirectReductionHealth", 0f), 0f, 10000f);
                    ArmourMinimumHealthPercent = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourMinimumHealthPercent", 0f), 0f, 100f);
                    ArmourResistancePowerHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourResistancePowerHealth", 0f), 0f, 1f);
                    ArmourResistanceMultiplierHealth = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourResistanceMultiplierHealth", 0f), 0f, 100000f);
                    ArmourAbsorptionBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourAbsorptionBleed", 1f), 0f, 10000f);
                    ArmourDirectReductionBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourDirectReductionBleed", 0f), 0f, 10000f);
                    ArmourResistancePowerBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourResistancePowerBleed", 0f), 0f, 1f);
                    ArmourResistanceMultiplierBleed = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourResistanceMultiplierBleed", 0f), 0f, 100000f);
                    ArmourMinimumBleedPercent = MathHelper.Clamp(ServerModAllCharacterSettings.GetAttributeFloat("ArmourMinimumBleedPercent", 0f), 0f, 100f);




                    //Player Settings
                    XElement ServerModPlayerSettings = doc.Root.Element("ServerModPlayerSettings");

                    PlayerCanTraumaDeath = ServerModPlayerSettings.GetAttributeBool("PlayerCanTraumaDeath", true); //Implemented
                    PlayerCanImplodeDeath = ServerModPlayerSettings.GetAttributeBool("PlayerCanImplodeDeath", true); //Implemented
                    PlayerCanSuffocateDeath = ServerModPlayerSettings.GetAttributeBool("PlayerCanSuffocateDeath", true); //Implemented
                    PlayerHealthMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerHealthMultiplier", 1f), 0.01f, 10000f); //Implemented
                    PlayerHuskHealthMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerHuskHealthMultiplier", 1f), 0.01f, 10000f); //Implemented
                    PlayerHuskAiOnDeath = ServerModPlayerSettings.GetAttributeBool("PlayerHuskAiOnDeath", true); //Implemented
                    PlayerHealthRegen = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerHealthRegen", 0f), 0f, 10000000f); //Implemented
                    PlayerHealthRegenMin = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerHealthRegenMin", -100f), -100f, 100f) / 100f; //Implemented
                    PlayerHealthRegenMax = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerHealthRegenMax", 100f), -100f, 100f) / 100f; //Implemented
                    PlayerCPROnlyWhileUnconcious = ServerModPlayerSettings.GetAttributeBool("PlayerCPROnlyWhileUnconcious", true); //Implemented
                    PlayerCPRHealthBaseValue = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRHealthBaseValue", 0f), -1000f, 1000f); //Implemented
                    PlayerCPRHealthSkillMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRHealthSkillMultiplier", 0f), -10f, 100f); //Implemented
                    PlayerCPRHealthSkillNeeded = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeInt("PlayerCPRHealthSkillNeeded", 100), 0, 100); //Implemented
                    PlayerCPRStunBaseValue = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRStunBaseValue", 0f), -1000f, 1000f); //Implemented
                    PlayerCPRStunSkillMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRStunSkillMultiplier", 0f), -10f, 100f); //Implemented
                    PlayerCPRStunSkillNeeded = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeInt("PlayerCPRStunSkillNeeded", 0), 0, 100); //Implemented
                    PlayerCPRClotBaseValue = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRClotBaseValue", 0f), -1000f, 1000f); //Implemented
                    PlayerCPRClotSkillMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPRClotSkillMultiplier", 0f), -10f, 100f); //Implemented
                    PlayerCPRClotSkillNeeded = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeInt("PlayerCPRClotSkillNeeded", 0), 0, 100); //Implemented
                    PlayerCPROxygenBaseValue = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPROxygenBaseValue", 0f), -1000f, 1000f); //Implemented
                    PlayerCPROxygenSkillMultiplier = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerCPROxygenSkillMultiplier", 0.1f), -10f, 100f); //Implemented
                    PlayerUnconciousTimer = MathHelper.Clamp(ServerModPlayerSettings.GetAttributeFloat("PlayerUnconciousTimer", 5f), 0f, 60f); //Implemented //Implemented

                    //Host Specific settings
                    XElement ServerModHostSettings = doc.Root.Element("ServerModHostSettings");

                    PlayYourselfName = ServerModHostSettings.GetAttributeString("PlayYourselfName", ""); //Implemented
                    HostBypassSkills = ServerModHostSettings.GetAttributeBool("HostBypassSkills", false); //Implemented

                    //Creature specific settings
                    XElement ServerModAICreatureSettings = doc.Root.Element("ServerModAICreatureSettings");

                    CreatureHealthMultiplier = MathHelper.Clamp(ServerModAICreatureSettings.GetAttributeFloat("CreatureHealthMultiplier", 1f), 0.01f, 1000000f);
                    CreatureHealthRegen = MathHelper.Clamp(ServerModAICreatureSettings.GetAttributeFloat("CreatureHealthRegen", 0f), 0f, 10000000f); //Implemented
                    CreatureHealthRegenMin = MathHelper.Clamp(ServerModAICreatureSettings.GetAttributeFloat("CreatureHealthRegenMin", 0f), 0f, 100f) / 100f;
                    CreatureHealthRegenMax = MathHelper.Clamp(ServerModAICreatureSettings.GetAttributeFloat("CreatureHealthRegenMax", 100f), 0f, 100f) / 100f;
                    CreatureEatDyingPlayers = ServerModAICreatureSettings.GetAttributeBool("CreatureEatDyingPlayers", true); //Implemented
                    CreatureRespawnMonsterEvents = ServerModAICreatureSettings.GetAttributeBool("CreatureRespawnMonsterEvents", true);
                    CreatureLimitRespawns = ServerModAICreatureSettings.GetAttributeBool("CreatureLimitRespawns", false);
                    CreatureMaxRespawns = Math.Min(Math.Max(ServerModAICreatureSettings.GetAttributeInt("CreatureMaxRespawns", 1), 1), 50);

                    //Nilmod Client specific settings
                    XElement ServerModClientSettings = doc.Root.Element("ServerModClientSettings");
                    AllowVanillaClients = ServerModClientSettings.GetAttributeBool("AllowVanillaClients", true);
                    AllowNilModClients = ServerModClientSettings.GetAttributeBool("AllowNilModClients", true);
                    cl_UseUpdatedCharHUD = ServerModClientSettings.GetAttributeBool("cl_UseUpdatedCharHUD", false);
                    cl_UseRecolouredNameInfo = ServerModClientSettings.GetAttributeBool("cl_UseRecolouredNameInfo", false);
                    cl_UseCreatureZoomBoost = ServerModClientSettings.GetAttributeBool("cl_UseCreatureZoomBoost", false);
                    cl_CreatureZoomMultiplier = MathHelper.Clamp(ServerModClientSettings.GetAttributeFloat("CreatureHealthMultiplier", 1f), 0.4f, 3f);


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
#if CLIENT
                        if((Screen.Selected is NetLobbyScreen))
                        {
                            NetLobbyScreen netlobby = (NetLobbyScreen)Screen.Selected;
                            netlobby.setHostName(PlayYourselfName);
                        }
#endif
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
            else
            {
                ResetToDefault(true);

                //This probagbly doesn't need to be done.
                //LoadComponants();
            }
        }

        public void Save()
        {
            List<string> lines = new List<string>
            {
                @"<?xml version=""1.0"" encoding=""utf-8"" ?>",
                "<NilMod>",
                "  <!--This is advanced configuration settings for your server stored by this modification!-->",
                "  <!--PLEASE NOTE: Anything inside these is not a setting but a comment.-->",

                "",

                "  <!--Scroll down under the actual settings to get the documentaiton on what they do.-->",
                "  <!--Also note this file re-writes itself, it is not possible to add comments but it will add new settings and remove old ones with updates.-->",
                "  <!--As a final note, if you want to ensure for a newer version of nilmod that the settings are DEFAULT for barotrauma - simply delete the lines (Not categories) from the XML, this will cause them to be re-added next launch as their default values.-->",

                "",

                "  <ServerModGeneralSettings",
                @"    BypassMD5=""" + BypassMD5 + @"""",
                @"    ServerMD5A=""" + ServerMD5A + @"""",
                @"    ServerMD5B=""" + ServerMD5B + @"""",
                @"    MaxAdminSlots=""" + MaxAdminSlots + @"""",
                @"    MaxModeratorSlots=""" + MaxModeratorSlots + @"""",
                @"    MaxSpectatorSlots=""" + MaxSpectatorSlots + @"""",
                @"    DebugConsoleTimeStamp=""" + DebugConsoleTimeStamp + @"""",
                @"    MaxLogMessages=""" + MaxLogMessages + @"""",
                @"    LogAppendCurrentRound=""" + LogAppendCurrentRound + @"""",
                @"    LogAppendLineSaveRate=""" + LogAppendLineSaveRate + @"""",
                @"    ClearLogRoundStart=""" + ClearLogRoundStart + @"""",
                @"    ChatboxHeight=""" + ChatboxHeight + @"""",
                @"    ChatboxWidth=""" + ChatboxWidth + @"""",
                @"    ChatboxMaxMessages=""" + ChatboxMaxMessages + @"""",
                @"    RebalanceTeamPreferences=""" + RebalanceTeamPreferences + @"""",
                @"    ShowRoomInfo=""" + ShowRoomInfo + @"""",
                @"    UseUpdatedCharHUD=""" + UseUpdatedCharHUD + @"""",
                @"    UseRecolouredNameInfo=""" + UseRecolouredNameInfo + @"""",
                @"    UseCreatureZoomBoost=""" + UseCreatureZoomBoost + @"""",
                @"    CreatureZoomMultiplier=""" + CreatureZoomMultiplier + @"""",
                @"    StartToServer=""" + StartToServer + @"""",
                @"    EnableEventChatterSystem=""" + EnableEventChatterSystem + @"""",
                @"    EnableHelpSystem=""" + EnableHelpSystem + @"""",
                @"    EnablePlayerLogSystem=""" + EnablePlayerLogSystem + @"""",
                @"    PlayerLogStateNames=""" + NilModPlayerLog.PlayerLogStateNames + @"""",
                @"    PlayerLogStateFirstJoinedNames=""" + NilModPlayerLog.PlayerLogStateFirstJoinedNames + @"""",
                @"    PlayerLogStateLastJoinedNames=""" + NilModPlayerLog.PlayerLogStateLastJoinedNames + @"""",
                @"    EnableVPNBanlist=""" + EnableVPNBanlist + @"""",
                @"    SubVotingConsoleLog=""" + SubVotingConsoleLog + @"""",
                @"    SubVotingServerLog=""" + SubVotingServerLog + @"""",
                @"    SubVotingAnnounce=""" + SubVotingAnnounce + @"""",
                @"    LogAIDamage=""" + LogAIDamage + @"""",
                @"    LogStatusEffectStun=""" + LogStatusEffectStun + @"""",
                @"    LogStatusEffectHealth=""" + LogStatusEffectHealth + @"""",
                @"    LogStatusEffectBleed=""" + LogStatusEffectBleed + @"""",
                @"    LogStatusEffectOxygen=""" + LogStatusEffectOxygen + @"""",
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
                @"    CampaignSaveName=""" + CampaignSaveName + @"""",
                //@"    SetDefaultsAlways=""" + SetDefaultsAlways + @"""",
                @"    UseAlternativeNetworking=""" + UseAlternativeNetworking + @"""",
                @"    CharacterDisabledistance=""" + CharacterDisabledistance + @"""",
                @"    ItemPosUpdateDistance=""" + ItemPosUpdateDistance + @"""",
                @"    DesyncTimerMultiplier=""" + DesyncTimerMultiplier + @"""",
                @"    DisableParticlesOnStart=""" + DisableParticlesOnStart + @"""",
                @"    DisableLightsOnStart=""" + DisableLightsOnStart + @"""",
                @"    DisableLOSOnStart=""" + DisableLOSOnStart + @"""",
                @"    AllowReconnect=""" + AllowReconnect + @"""",
                @"    ReconnectAddStun=""" + ReconnectAddStun + @"""",
                @"    ReconnectTimeAllowed=""" + ReconnectTimeAllowed + @"""",
                @"    UseDesyncPrevention=""" + UseDesyncPrevention + @"""",
                @"    DesyncPreventionItemPassTimer=""" + DesyncPreventionItemPassTimer + @"""",
                @"    DesyncPreventionPassItemCount=""" + DesyncPreventionPassItemCount + @"""",
                @"    DesyncPreventionPlayerStatusTimer=""" + DesyncPreventionPlayerStatusTimer + @"""",
                @"    DesyncPreventionPassPlayerStatusCount=""" + DesyncPreventionPassPlayerStatusCount + @"""",
                @"    DesyncPreventionHullStatusTimer=""" + DesyncPreventionHullStatusTimer + @"""",
                @"    DesyncPreventionPassHullCount=""" + DesyncPreventionPassHullCount + @"""",
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
                @"    OceanColourR=""" + WaterColour.R + @"""",
                @"    OceanColourG=""" + WaterColour.G + @"""",
                @"    OceanColourB=""" + WaterColour.B + @"""",
                @"    OceanColourA=""" + WaterColour.A + @"""",
                @"    HullOxygenDistributionSpeed=""" + HullOxygenDistributionSpeed + @"""",
                @"    HullOxygenDetoriationSpeed=""" + HullOxygenDetoriationSpeed + @"""",
                @"    HullOxygenConsumptionSpeed=""" + HullOxygenConsumptionSpeed + @"""",
                @"    HullUnbreathablePercent=""" + HullUnbreathablePercent + @"""",
                @"    CanDamageSubBody=""" + CanDamageSubBody + @"""",
                @"    CanRewireMainSubs=""" + CanRewireMainSubs + @"""",
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

                "  <!--These Settings effect Clients that join the server, more specifically Nilmod clients and ability to connect-->",
                "  <ServerModClientSettings",
                @"    AllowVanillaClients=""" + AllowVanillaClients + @"""",
                @"    AllowNilModClients=""" + AllowNilModClients + @"""",
                @"    cl_UseUpdatedCharHUD=""" + cl_UseUpdatedCharHUD + @"""",
                @"    cl_UseRecolouredNameInfo=""" + cl_UseRecolouredNameInfo + @"""",
                @"    cl_UseCreatureZoomBoost=""" + cl_UseCreatureZoomBoost + @"""",
                @"    cl_CreatureZoomMultiplier=""" + cl_CreatureZoomMultiplier + @"""",
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

                "  <!--BypassMD5 = Setting to change server from calculating MD5 normally to using the ServerMD5 setting file instead-->",
                "  <!--ServerMD5A = The server content folders MD5 - used for players using the same content mod to connect despite edits (Read above for what you shouldn't edit), Connect with a client to see the MD5 comparison and adjust to the clients MD5-->",
                "  <!--ServerMD5B = A Secondary MD5 incase you wish to have two sets of files that may join a server, this is more towards servers that modify and hand out their package, but tweak it further later.-->",
                "  <!--MaxAdminSlots = Players whom have the Ban Client Permission bypass slots joining the server, if this is set to 2, 2 may join beyond the maxplayers and not count in the server list.-->",
                "  <!--MaxModeratorSlots = Players whom have the Kick Client Permission bypass slots joining the server, if this is set to 2, 2 may join beyond the maxplayers and not count in the server list.-->",
                "  <!--MaxSpectatorSlots = Players whom are permanent spectators will be counted in this, allowing more players past the cap and not counting them unless they toggle it off (2 spectators removing it would result in 18/16), admins currently are also counted as spectators if their spectating too but not while they play. if MaxPlayers is exceeded and they stop being spectators it does not allow more in regardless-->",
                "  <!--DebugConsoleTimeStamp = Adds a timestamp to the console messages.-->",
                "  <!--MaxLogMessages = The max messages the ingame server log will keep.-->",
                "  <!--LogAppendCurrentRound = Enables the server log to be written in real time, one file per-round.-->",
                "  <!--LogAppendLineSaveRate = The rate at which the above setting saves (Every X lines).-->",
                "  <!--ClearLogRoundStart = At the start of a round, clears the ingame server log (And if unsaved, saves the remainder too).-->",
                "  <!--ChatboxHeight = The Height of your chatbox (Top to bottom).-->",
                "  <!--ChatboxWidth = The Length of your chatbox (Side-to-side).-->",
                "  <!--ChatboxMaxMessages = The maximum count of messages your chatbox can store before it begins removing them.-->",
                "  <!--RebalanceTeamPreferences = The playerlist to the server host may now define Preferred teams, this is an override to prevent team rebalancing if one team has more players than another.-->",
                "  <!--ShowRoomInfo = Adds the Oxygen and volume text to rooms without the use of DebugDraw.-->",
                "  <!--UseUpdatedCharHUD = Replaces the games Health bars with more detailed ones, including oxygen, Bleed, pressure, stun and husk, where applicable. The bars also pulse to the condition of the target.-->",
                "  <!--UseRecolouredNameInfo = Recolour character names from the standard white to red (renegades/enemy team of char)/blue (Coalition/same team of char)/white(No team/AIs) and darker varients, along with purple (Husk infection)-->",
                "  <!--UseCreatureZoomBoost = Modifies the camera zoom level for creatures based on their size and the zoom multiplier, its not perfect but for huge creatures it keeps the camera zoomed out further.-->",
                "  <!--CreatureZoomMultiplier = Used in the above setting, this is a multiplier on the effect of the zoom boost, its min to max value is 0.4 - 3.0-->",
                "  <!--StartToServer =-->",
                "  <!--EnableEventChatterSystem =-->",
                "  <!--EnableHelpSystem =-->",
                "  <!--EnablePlayerLogSystem =-->",
                "  <!--PlayerLogStateNames =-->",
                "  <!--PlayerLogStateFirstJoinedNames=-->",
                "  <!--PlayerLogStateLastJoinedNames=-->",
                "  <!--EnableVPNBanlist=-->",
                "  <!--SubVotingConsoleLog=-->",
                "  <!--SubVotingServerLog=-->",
                "  <!--SubVotingAnnounce=-->",
                "  <!--LogAIDamage=-->",
                "  <!--LogStatusEffectStun=-->",
                "  <!--LogStatusEffectHealth=-->",
                "  <!--LogStatusEffectBleed=-->",
                "  <!--LogStatusEffectOxygen=-->",
                "  <!--CrashRestart=-->",
                "  <!--UseStartWindowPosition=-->",
                "  <!--StartXPos=-->",
                "  <!--StartYPos=-->",
                "  <!--BanListReloadTimer=-->",
                "  <!--BansOverrideBannedInfo=-->",
                "  <!--BansInfoCustomtext=-->",
                "  <!--BansInfoAddBanName=-->",
                "  <!--BansInfoAddBanDuration=-->",
                "  <!--BansInfoUseRemainingTime=-->",
                "  <!--BansInfoAddCustomString=-->",
                "  <!--BansInfoAddBanReason=-->",
                "  <!--VoteKickStateNameTimer=-->",
                "  <!--VoteKickDenyRejoinTimer=-->",
                "  <!--AdminKickStateNameTimer=-->",
                "  <!--AdminKickDenyRejoinTimer=-->",
                "  <!--KickStateNameTimerIncreaseOnRejoin=-->",
                "  <!--KickMaxStateNameTimer=-->",
                "  <!--ClearKickStateNameOnRejoin=-->",
                "  <!--ClearKicksOnRoundStart=-->",

                "  <!--SuppressPacketSizeWarning = Suppresses an error that annoys server hosts but is actually mostly harmless anyways, Default=false-->",
                "  <!--StartToServer = Setting to use the server default settings when starting NilMod - for now please use actually valid settings XD, Default=false-->",

                "",

                //"  <!--MaxRespawns = number of times the respawn shuttle may appear before it ceases to spawn 0=Infinite, default=0-->",
                //"  <!--RespawnOnMainSub = Instantly places newly respawned players into the mainsub at an appropriate spawn point, good for Deathmatching, Default=false-->",
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

        public void ResetToDefault(Boolean ClientMode = false)
        {
            //Core Settings
            BypassMD5 = false;
            ServerMD5A = GameMain.SelectedPackage.MD5hash.Hash;
            ServerMD5B = "";
            MaxAdminSlots = 0;
            MaxModeratorSlots = 0;
            MaxSpectatorSlots = 0;
            if (!ClientMode) DebugConsoleTimeStamp = false;
            MaxLogMessages = 800;
            LogAppendCurrentRound = false;
            LogAppendLineSaveRate = 5;
            ClearLogRoundStart = false;
            if (!ClientMode)
            {
                ChatboxHeight = 0.15f;
                ChatboxWidth = 0.35f;
                ChatboxMaxMessages = 20;
            }
            RebalanceTeamPreferences = true;
            ShowRoomInfo = false;
            UseUpdatedCharHUD = false;
            UseRecolouredNameInfo = false;
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
            LogStatusEffectStun = false;
            LogStatusEffectHealth = false;
            LogStatusEffectBleed = false;
            LogStatusEffectOxygen = false;
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
            CampaignSaveName = "";
            SetDefaultsAlways = false;
            UseAlternativeNetworking = false;
            CharacterDisabledistance = 20000.0f;
            NetConfig.CharacterIgnoreDistance = CharacterDisabledistance;
            NetConfig.CharacterIgnoreDistanceSqr = CharacterDisabledistance * CharacterDisabledistance;
            ItemPosUpdateDistance = 2.00f;
            NetConfig.ItemPosUpdateDistance = ItemPosUpdateDistance;
            DesyncTimerMultiplier = 1.00f;
            if(!ClientMode) DisableParticlesOnStart = false;
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
            if (!ClientMode)
            {
                MaxParticles = 1500;
                ParticleSpawnPercent = 100;
                ParticleLifeMultiplier = 1.0f;
            }
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

            UseDesyncPrevention = true;
            DesyncPreventionItemPassTimer = 0.15f;
            DesyncPreventionPassItemCount = 10;
            DesyncPreventionPlayerStatusTimer = 0.5f;
            DesyncPreventionPassPlayerStatusCount = 1;
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
            WaterColour = new Color(191, 204, 230, 255);
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
            ArmourAbsorptionHealth = 1f;
            ArmourDirectReductionHealth = 0f;
            ArmourMinimumHealthPercent = 0f;
            ArmourResistancePowerHealth = 0f;
            ArmourResistanceMultiplierHealth = 0f;
            ArmourAbsorptionBleed = 1f;
            ArmourDirectReductionBleed = 0f;
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

            //Client Settings
            AllowVanillaClients = true;
            AllowNilModClients = true;
            cl_UseUpdatedCharHUD = false;
            cl_UseRecolouredNameInfo = false;
            cl_UseCreatureZoomBoost = false;
            cl_CreatureZoomMultiplier = 1f;

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

        public void TestArmour(float Modifier = 1f)
        {
            float armourrating = (1f - Modifier) * 100f;
            DebugConsole.NewMessage("ArmourDebug Test Calculations (Does not factor bleed bypass or impossible reverse damage):", Color.White);
            //1 armour damage reduction console messages
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 001, Bleed: 00.25 Damage Taken: "
                + ArmourCheckHealth(1f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(0.25f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 003, Bleed: 00.50 Damage Taken: "
                + ArmourCheckHealth(3f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(0.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 005, Bleed: 00.75 Damage Taken: "
                + ArmourCheckHealth(5f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(0.75f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 010, Bleed: 01.00 Damage Taken: "
                + ArmourCheckHealth(10f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(1f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 015, Bleed: 01.25 Damage Taken: "
                + ArmourCheckHealth(15f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(1.25f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 020, Bleed: 01.50 Damage Taken: "
                + ArmourCheckHealth(20f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(1.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 025, Bleed: 01.75 Damage Taken: "
                + ArmourCheckHealth(25f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(1.75f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 030, Bleed: 02.00 Damage Taken: "
                + ArmourCheckHealth(30f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(2f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 035, Bleed: 02.25 Damage Taken: "
                + ArmourCheckHealth(35f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(2.25f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 040, Bleed: 02.50 Damage Taken: "
                + ArmourCheckHealth(40f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(2.50f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 045, Bleed: 03.00 Damage Taken: "
                + ArmourCheckHealth(45f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(3f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 050, Bleed: 03.50 Damage Taken: "
                + ArmourCheckHealth(50f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(3.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 060, Bleed: 04.00 Damage Taken: "
                + ArmourCheckHealth(60f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(4f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 070, Bleed: 05.00 Damage Taken: "
                + ArmourCheckHealth(70f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 080, Bleed: 06.00 Damage Taken: "
                + ArmourCheckHealth(80f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(6f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 090, Bleed: 07.50 Damage Taken: "
                + ArmourCheckHealth(90f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(7.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 100, Bleed: 10.00 Damage Taken: "
                + ArmourCheckHealth(100f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(10f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 125, Bleed: 12.50 Damage Taken: "
                + ArmourCheckHealth(125f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(12.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 150, Bleed: 15.00 Damage Taken: "
                + ArmourCheckHealth(150f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(15f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 175, Bleed: 17.50 Damage Taken: "
                + ArmourCheckHealth(175f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(17.5f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 200, Bleed: 20.00 Damage Taken: "
                + ArmourCheckHealth(200f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(20f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 250, Bleed: 25.00 Damage Taken: "
                + ArmourCheckHealth(250f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(25f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 300, Bleed: 30.00 Damage Taken: "
                + ArmourCheckHealth(300f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(30f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 350, Bleed: 40.00 Damage Taken: "
                + ArmourCheckHealth(350f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(40f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 400, Bleed: 50.00 Damage Taken: "
                + ArmourCheckHealth(400f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(50f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 500, Bleed: 60.00 Damage Taken: "
                + ArmourCheckHealth(500f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(60f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 600, Bleed: 75.00 Damage Taken: "
                + ArmourCheckHealth(600f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(75f, Modifier), Color.White);
            DebugConsole.NewMessage("Armour: " + armourrating + " Damage: 800, Bleed: 90.00 Damage Taken: "
                + ArmourCheckHealth(800f, Modifier) + " Bleed Taken: " + ArmourCheckBleed(90f, Modifier), Color.White);
                
        }

        float ArmourCheckHealth(float healthdamage, float modifier)
        {
            float fakehealth = 25000f;
            float fakehealthdamage = 0f;

            fakehealth = fakehealth - Limb.CalculateNewHealth(healthdamage, healthdamage, modifier);

            fakehealthdamage = 25000f - fakehealth;

            return Convert.ToSingle(Math.Round(fakehealthdamage, 2));
        }

        float ArmourCheckBleed(float bleeddamage, float modifier)
        {
            float fakebleed = 500f;
            float fakebleeddamage = 0f;

            fakebleed = fakebleed - Limb.CalculateNewBleed(bleeddamage, bleeddamage,modifier);

            fakebleeddamage = 500f - fakebleed;

            return Convert.ToSingle(Math.Round(fakebleeddamage, 2));
        }

        //Things that are generally done at round start or server start for the server.
        //Note, need to move more code here to clean up project someday (Such as mission selection preferably).
        public void GameInitialize(Boolean InitialLaunch = false)
        {
            if (InitialLaunch)
            {
                if (GameMain.Server != null)
                {
                    GameMain.Server.AutoRestart = AutoRestart;
                }

                if (GameMain.Client == null)
                {
                    if (DisableParticlesOnStart) DisableParticles = true;
                }
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

            if (FrozenCharacters != null)
            {
                FrozenCharacters.Clear();
            }
            else
            {
                FrozenCharacters = new List<Character>();
            }

            //We should allow this in single player - but not in multiplayer.
            if (GameMain.Client == null)
            {
                
            }
            //If their a client a few things shouldn't carry over from singleplayer / self hosting.
            else
            {
                DisableParticles = false;
            }

            if (GameMain.Server != null)
            {
                if (KickedClients != null)
                {
                    if (ClearKicksOnRoundStart) KickedClients.Clear();
                }
                else
                {
                    KickedClients = new List<KickedClient>();
                }

                DesyncPreventionItemList = new List<Item>();
                DesyncPreventionPlayerStatusList = new List<Character>();
                DesyncPreventionHullList = new List<Hull>();
                DesyncPreventionItemPassTimerleft = 30f + DesyncPreventionItemPassTimer;
                DesyncPreventionPlayerStatusTimerleft = 30f + DesyncPreventionPlayerStatusTimer;
                DesyncPreventionHullStatusTimerleft = 30f + DesyncPreventionHullStatusTimerleft;
            }
        }

        public void HideCharacter(Character character)
        {
            //Only execute this code for Servers - Use normal removal for single player
            if (GameMain.Server != null)
            {
#if CLIENT
                GameSession.inGameInfo.RemoveCharacter(character);
#endif

                ConvertingHusk huskconvert = new ConvertingHusk();
                huskconvert.character = character;
                huskconvert.Updatestildisable = 2;
                //Hide the character completely
                character.AnimController.CurrentHull = null;
                character.Submarine = null;
                character.AnimController.SetPosition(new Vector2(800000, 800000), false);
                huskconvert.character.HuskInfectionState = 0f;
                GameMain.NilMod.convertinghusklist.Add(huskconvert);
            }
            else if (GameMain.Client == null)
            {
#if CLIENT
                GameSession.inGameInfo.RemoveCharacter(character);
#endif

                //Just add them to the entity remover if single player
                Entity.Spawner.AddToRemoveQueue(character);
            }
        }

        //Code for reading nilmod settings sync data
        public void ClientSyncRead(Lidgren.Network.NetIncomingMessage inc)
        {
            Lidgren.Network.NetOutgoingMessage outmsg = GameMain.Server.server.CreateMessage();

            outmsg.Write((Byte)ClientPacketHeader.NILMODSYNCRECEIVED);

            Version serverversion = new Version(inc.ReadString());
            //Same networking version
            if (NilModNetworkingVersion.CompareTo(serverversion) == 0)
            {
                //Client Settings
                UseUpdatedCharHUD = inc.ReadBoolean();
                UseUpdatedCharHUD = inc.ReadBoolean();
                UseCreatureZoomBoost = inc.ReadBoolean();
                CreatureZoomMultiplier = inc.ReadFloat();

                //Submarine Settings
                Byte OceanColourR;
                Byte OceanColourG;
                Byte OceanColourB;
                Byte OceanColourA;
                OceanColourR = inc.ReadByte();
                OceanColourG = inc.ReadByte();
                OceanColourB = inc.ReadByte();
                OceanColourA = inc.ReadByte();
                WaterColour = new Color(OceanColourR, OceanColourG, OceanColourB, OceanColourA);

                HullOxygenDistributionSpeed = inc.ReadFloat();
                HullOxygenDetoriationSpeed = inc.ReadFloat();
                HullOxygenConsumptionSpeed = inc.ReadFloat();
                HullUnbreathablePercent = inc.ReadFloat();
                CrushDamageDepth = inc.ReadFloat();
                PlayerCrushDepthInHull = inc.ReadFloat();
                PlayerCrushDepthOutsideHull = inc.ReadFloat();
                FireOxygenConsumption = inc.ReadFloat();
                FireGrowthSpeed = inc.ReadFloat();
                FireShrinkSpeed = inc.ReadFloat();
                FireWaterExtinguishMultiplier = inc.ReadFloat();
                FireToolExtinguishMultiplier = inc.ReadFloat();


                //Character Settings
                PlayerOxygenUsageAmount = inc.ReadFloat();
                PlayerOxygenGainSpeed = inc.ReadFloat();
                PlayerUnconciousTimer = inc.ReadFloat();



                //End of message
                inc.ReadByte();
                //Sync successful
                outmsg.Write((Byte)0);
            }
            else if(NilModNetworkingVersion.CompareTo(serverversion) >= 1)
            {
                //Sync failed
                outmsg.Write((Byte)2);
                DebugConsole.ThrowError("Nilmod attempted and failed client sync, server is using a previous version.");
                DebugConsole.ThrowError("Nilmod is now in vanilla setup.");
                DebugConsole.ThrowError("Press the F3 key to close the console.");
            }
            //Client is Earlier version than server
            else if (NilModNetworkingVersion.CompareTo(serverversion) <= -1)
            {
                //Sync failed (server is higher version)
                outmsg.Write((Byte)1);
                DebugConsole.ThrowError("Nilmod attempted and failed client sync, server is using a newer version.");
                DebugConsole.ThrowError("Nilmod is now in vanilla setup.");
                DebugConsole.ThrowError("Press the F3 key to close the console.");
            }



            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);
        }

        //Code for writing nilmod settings sync data packets
        public void ServerSyncWrite(Client c)
        {
            Lidgren.Network.NetOutgoingMessage outmsg = GameMain.Server.server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.NILMODSYNC);

            //Send the networking version of the server
            outmsg.Write(NilModNetworkingVersion.ToString());

            //Client Settings
            outmsg.Write(cl_UseUpdatedCharHUD);
            outmsg.Write(cl_UseRecolouredNameInfo);
            outmsg.Write(cl_UseCreatureZoomBoost);
            outmsg.Write(cl_CreatureZoomMultiplier);

            //Submarine Settings
            outmsg.Write((byte)WaterColour.R);
            outmsg.Write((byte)WaterColour.G);
            outmsg.Write((byte)WaterColour.B);
            outmsg.Write((byte)WaterColour.A);

            outmsg.Write(HullOxygenDistributionSpeed);
            outmsg.Write(HullOxygenDetoriationSpeed);
            outmsg.Write(HullOxygenConsumptionSpeed);
            outmsg.Write(HullUnbreathablePercent);

            outmsg.Write(CrushDamageDepth);
            outmsg.Write(PlayerCrushDepthInHull);
            outmsg.Write(PlayerCrushDepthOutsideHull);

            outmsg.Write(FireOxygenConsumption);
            outmsg.Write(FireGrowthSpeed);
            outmsg.Write(FireShrinkSpeed);
            outmsg.Write(FireWaterExtinguishMultiplier);
            outmsg.Write(FireToolExtinguishMultiplier);


            //Character Settings
            outmsg.Write(PlayerOxygenUsageAmount);
            outmsg.Write(PlayerOxygenGainSpeed);
            outmsg.Write(PlayerUnconciousTimer);

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            GameMain.Server.server.SendMessage(outmsg, c.Connection, Lidgren.Network.NetDeliveryMethod.Unreliable);
        }

        public void FetchExternalIP()
        {
            ExternalIPWebClient = new System.Net.WebClient();
            ExternalIP = "";

            ExternalIPWebClient.DownloadStringAsync(new Uri("http://checkip.dyndns.org/"));
            ExternalIPWebClient.DownloadStringCompleted += new System.Net.DownloadStringCompletedEventHandler(ReceivedExternalIPResponse);
            //externalip = new System.Net.WebClient().DownloadString("http://checkip.dyndns.org/");
        }

        public void ReceivedExternalIPResponse(object sender, System.Net.DownloadStringCompletedEventArgs e)
        {
            if (!e.Cancelled && e.Error == null)
            {
                ExternalIP = e.Result;
                ExternalIP = ExternalIP.Replace("<html><head><title>Current IP Check</title></head><body>Current IP Address: ", "");
                ExternalIP = ExternalIP.Replace("</body></html>\r\n", "");
                ExternalIP = ExternalIP.Trim();

#if SERVER
                DebugConsole.NewMessage(" ", Color.Cyan);
                DebugConsole.NewMessage("External IP Retrieved successfully.", Color.Cyan);
                GameMain.Server.StateServerInfo();
#endif
            }
            else
            {
                DebugConsole.NewMessage("NILMOD ERROR - Failed to retrieve External IP: " + Environment.NewLine + e.Error.Message, Color.Red);
                ExternalIP = "?.?.?.?";
            }
            ExternalIPWebClient = null;
        }

        public List<Client> RandomizeClientOrder(List<Client> clientsource)
        {
            List<Client> randList = new List<Client>(clientsource);
            for (int i = 0; i < randList.Count; i++)
            {
                Client a = randList[i];
                int oi = Rand.Range(0, randList.Count - 1);
                Client b = randList[oi];
                randList[i] = b;
                randList[oi] = a;
            }

            return randList;
        }
    }
}
