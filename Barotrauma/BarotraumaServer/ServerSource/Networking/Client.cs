using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class Client : IDisposable
    {
        public bool VoiceEnabled = true;

        public VoipServerDecoder VoipServerDecoder;

        public UInt16 LastRecvClientListUpdate
            = NetIdUtils.GetIdOlderThan(GameMain.Server.LastClientListUpdateID);

        public UInt16 LastSentServerSettingsUpdate
            = NetIdUtils.GetIdOlderThan(GameMain.Server.ServerSettings.LastUpdateIdForFlag[ServerSettings.NetFlags.Properties]);
        public UInt16 LastRecvServerSettingsUpdate
            = NetIdUtils.GetIdOlderThan(GameMain.Server.ServerSettings.LastUpdateIdForFlag[ServerSettings.NetFlags.Properties]);

        public UInt16 LastRecvLobbyUpdate
            = NetIdUtils.GetIdOlderThan(GameMain.NetLobbyScreen.LastUpdateID);

        public UInt16 LastSentChatMsgID = 0; //last msg this client said
        public UInt16 LastRecvChatMsgID = 0; //last msg this client knows about

        public UInt16 LastSentEntityEventID = 0;
        public UInt16 LastRecvEntityEventID = 0;

        public readonly Dictionary<MultiPlayerCampaign.NetFlags, UInt16> LastRecvCampaignUpdate
            = new Dictionary<MultiPlayerCampaign.NetFlags, UInt16>();
        public UInt16 LastRecvCampaignSave = 0;

        public (UInt16 saveId, float time) LastCampaignSaveSendTime;

        public readonly List<ChatMessage> ChatMsgQueue = new List<ChatMessage>();
        public UInt16 LastChatMsgQueueID;

        //latest chat messages sent by this client
        public readonly List<string> LastSentChatMessages = new List<string>();
        public float ChatSpamSpeed;
        public float ChatSpamTimer;
        public int ChatSpamCount;

        public string RejectedName;

        public float KickAFKTimer;

        public double MidRoundSyncTimeOut;

        public bool NeedsMidRoundSync;
        //how many unique events the client missed before joining the server
        public UInt16 UnreceivedEntityEventCount;
        public UInt16 FirstNewEventID;
        
        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public readonly Dictionary<UInt16, double> EntityEventLastSent = new Dictionary<UInt16, double>();
        
        //when was a position update for a given entity last sent to the client
        //  key = entity, value = NetTime.Now when sending
        public readonly Dictionary<Entity, float> PositionUpdateLastSent = new Dictionary<Entity, float>();
        public readonly Queue<Entity> PendingPositionUpdates = new Queue<Entity>();

        public bool ReadyToStart;

        public List<JobVariant> JobPreferences { get; set; }
        public JobVariant AssignedJob;

        public float DeleteDisconnectedTimer;

        public DateTime JoinTime;

        public static readonly TimeSpan NameChangeCoolDown = new TimeSpan(hours: 0, minutes: 0, seconds: 30);
        public DateTime LastNameChangeTime;

        private CharacterInfo characterInfo;
        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set
            {
                if (characterInfo == value) { return; }
                characterInfo?.Remove();
                characterInfo = value;
            }
        }

        public string PendingName;

        public NetworkConnection Connection { get; set; }

        public bool SpectateOnly;
        public bool? WaitForNextRoundRespawn;

        public int KarmaKickCount;

        private float syncedKarma = 100.0f;
        private float karma = 100.0f;
        public float Karma
        {
            get
            {
                if (GameMain.Server == null || !GameMain.Server.ServerSettings.KarmaEnabled) { return 100.0f; }
                if (HasPermission(ClientPermissions.KarmaImmunity)) { return 100.0f; }
                return karma;
            }
            set
            {
                if (GameMain.Server == null || !GameMain.Server.ServerSettings.KarmaEnabled) { return; }
                karma = Math.Min(Math.Max(value, 0.0f), 100.0f);
                if (!MathUtils.NearlyEqual(karma, syncedKarma, 10.0f))
                {
                    syncedKarma = karma;
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
            }
        }

        private List<Client> kickVoters;

        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }

        partial void InitProjSpecific()
        {
            kickVoters = new List<Client>();

            JobPreferences = new List<JobVariant>();

            VoipQueue = new VoipQueue(SessionId, true, true);
            VoipServerDecoder = new VoipServerDecoder(VoipQueue, this);
            GameMain.Server.VoipServer.RegisterQueue(VoipQueue);

            //initialize to infinity, gets set to a proper value when initializing midround syncing
            MidRoundSyncTimeOut = double.PositiveInfinity;

            JoinTime = DateTime.Now;
        }

        partial void DisposeProjSpecific()
        {
            GameMain.Server.VoipServer.UnregisterQueue(VoipQueue);
            VoipQueue.Dispose();
            if (characterInfo != null)
            {
                if (characterInfo.Character == null || characterInfo.Character.Removed)
                {
                    characterInfo?.Remove();
                    characterInfo = null;
                }
            }
        }

        public void InitClientSync()
        {
            LastSentChatMsgID = 0;
            LastRecvChatMsgID = ChatMessage.LastID;

            LastRecvLobbyUpdate = 0;

            LastRecvEntityEventID = 0;

            UnreceivedEntityEventCount = 0;
            NeedsMidRoundSync = false;
        }

        public static bool IsValidName(string name, ServerSettings serverSettings)
        {
            if (string.IsNullOrWhiteSpace(name)) { return false; }
            
            char[] disallowedChars =
            {
                //',', //previously disallowed because of the ban list format
                
                ';',
                '<',
                '>',
                
                '/', //disallowed because of server messages using forward slash as a delimiter (TODO: implement escaping)
                
                '\\',
                '[',
                ']',
                '"',
                '?'
            };
            if (name.Any(c => disallowedChars.Contains(c))) { return false; }

            foreach (char character in name)
            {
                if (!serverSettings.AllowedClientNameChars.Any(charRange => (int)character >= charRange.Start && (int)character <= charRange.End)) { return false; }
            }

            return true;
        }

        public bool AddressMatches(Address address)
        {
            return Connection.Endpoint.Address.Equals(address);
        }

        public void AddKickVote(Client voter)
        {
            if (voter != null && !kickVoters.Contains(voter)) { kickVoters.Add(voter); }
        }

        public void RemoveKickVote(Client voter)
        {
            kickVoters.Remove(voter);
        }

        public bool HasKickVoteFrom(Client voter)
        {
            return kickVoters.Contains(voter);
        }

        public bool HasKickVoteFromSessionId(int id)
        {
            return kickVoters.Any(k => k.SessionId == id);
        }

        public static void UpdateKickVotes(IReadOnlyList<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                client.kickVoters.RemoveAll(voter => !connectedClients.Contains(voter));
            }
        }

        /// <summary>
        /// Reset what this client has voted for and the kick votes given to this client
        /// </summary>
        public void ResetVotes(bool resetKickVotes)
        {
            for (int i = 0; i < votes.Length; i++)
            {
                votes[i] = null;
            }
            if (resetKickVotes)
            {
                kickVoters.Clear();
            }
        }


        public void SetPermissions(ClientPermissions permissions, IEnumerable<DebugConsole.Command> permittedConsoleCommands)
        {
            Permissions = permissions;
            PermittedConsoleCommands.Clear();
            PermittedConsoleCommands.UnionWith(permittedConsoleCommands);
            if (Permissions.HasFlag(ClientPermissions.ManageSettings))
            {
                //ensure the client has the up-to-date server settings
                GameMain.Server?.ServerSettings?.ForcePropertyUpdate();
            }
        }

        public void GivePermission(ClientPermissions permission)
        {
            if (!Permissions.HasFlag(permission))
            {
                Permissions |= permission;
                if (permission.HasFlag(ClientPermissions.ManageSettings))
                {
                    //ensure the client has the up-to-date server settings
                    GameMain.Server?.ServerSettings?.ForcePropertyUpdate();
                }
            }
        }

        public void RemovePermission(ClientPermissions permission)
        {
            Permissions &= ~permission;
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return Permissions.HasFlag(permission);
        }

        public bool TryTakeOverBot(Character botCharacter)
        {
            if (GameMain.Server == null)
            {
                DebugConsole.ThrowError($"TryTakeOverBot: Client {Name} requested to take over a bot but GameMain.Server is null!");
                return false;
            }
            if (GameMain.NetworkMember is not { ServerSettings.RespawnMode: RespawnMode.Permadeath })
            {
                DebugConsole.ThrowError($"Client {Name} requested to take over a bot but Permadeath is not enabled!");
                GameMain.Server.SendConsoleMessage($"Permadeath mode is not enabled, cannot take over a bot.", this, Color.Red);
                return false;
            }
            if (CharacterInfo == null)
            {
                DebugConsole.ThrowError($"Permadeath: Client {Name} requested to take over a bot, but they don't seem to have a character at all yet.");
                GameMain.Server.SendConsoleMessage($"Permadeath: Taking over a bot requires having a character that died first.", this, Color.Red);
                return false;
            }
            if (CharacterInfo is not { PermanentlyDead: true })
            {
                DebugConsole.ThrowError($"Permadeath: Client {Name} requested to take over a bot, but their character has not been permanently killed.");
                GameMain.Server.SendConsoleMessage($"Permadeath: Could not take over the bot, previous character not permanently killed.", this, Color.Red);
                return false;
            }
            if (!botCharacter.IsBot)
            {
                DebugConsole.ThrowError($"Permadeath: {Name} requested to take over a bot character, but the target character is not a bot!");
                GameMain.Server.SendConsoleMessage($"Permadeath: Could not take over the target character because it is not a bot.", this, Color.Red);
                return false;
            }

            if (botCharacter.Info != null)
            {
                botCharacter.Info.RenamingEnabled = true; // Grant one opportunity to rename a taken over bot
            }

            // Now that the old permanently killed character will be replaced, we can fully discard it
            var mpCampaign = GameMain.GameSession?.Campaign as MultiPlayerCampaign;
            mpCampaign?.DiscardClientCharacterData(this);
            GameMain.Server.SetClientCharacter(this, botCharacter);
            if (mpCampaign?.SetClientCharacterData(this) is CharacterCampaignData characterData)
            {
                //the bot has spawned, but the new CharacterCampaignData technically hasn't, because we just created it
                characterData.HasSpawned = true;
            }

            SpectateOnly = false;
            return true;
        }
    }
}
