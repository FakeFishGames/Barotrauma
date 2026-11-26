using Barotrauma.Networking;
using System;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Modifies the current location in some way (e.g. adjusting the faction, type of name).
    /// </summary>
    class ModifyLocationAction : EventAction
    {
        public enum Target
        {
            /// <summary>
            /// Default behavior: modifies the current location.
            /// </summary>
            Default,
            /// <summary>
            /// Only valid for actions executed by a mission. Modifies the start location of the triggering mission.
            /// </summary>
            MissionStartLocation,
            /// <summary>
            /// Only valid for actions executed by a mission. Modifies the end location (destination) of the triggering mission.
            /// </summary>
            MissionEndLocation,
            /// <summary>
            /// Searches for a location type specified in <see cref="TargetLocationType"/> and modifies that location instead (if there's multiple, the first one found is chosen)
            /// </summary>
            LocationType,
        }

        [Serialize(Target.Default, IsPropertySaveable.Yes)]
        public Target TargetLocation { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction to set as the location's primary faction (optional).")]
        public Identifier Faction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the faction to set as the location's secondary faction (optional).")]
        public Identifier SecondaryFaction { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Resets the list of previously killed characters in the location, which means the population can be set to a specific value without it being reduced by recent deaths.")]
        public bool ResetKilledCharacters { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the location type to set as the location's new type (optional)")]
        public Identifier Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: $"Identifier of the location with the specific type that should be changed, only relevant when the {nameof(TargetLocation)} is set to {nameof(Target.LocationType)}.")]
        public Identifier TargetLocationType { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "New name to give to the location (optional). Can either be the name as-is, or a tag referring to a line in a text file.")]
        public Identifier Name { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, all the currently available missions are cleared from the location.")]
        public bool ClearMissions { get; set; }

        private bool isFinished;

        public ModifyLocationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                Location location = TargetLocation switch
                {
                    Target.Default => campaign.CurrentLocation,
                    Target.MissionStartLocation => ParentMission?.Locations[0] ?? campaign.CurrentLocation,
                    Target.MissionEndLocation => ParentMission?.Locations[1] ?? campaign.CurrentLocation,
                    Target.LocationType => campaign.Map.Locations.FirstOrDefault(location => location.Type.Identifier == TargetLocationType),
                    _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
                };
                if (location != null) 
                {
                    bool factionChanged = false;
                    bool typeChanged = false;

                    if (!Faction.IsEmpty)
                    {
                        var faction = campaign.GetFaction(Faction);
                        if (faction == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{Faction}\".",
                                contentPackage: ParentEvent?.Prefab?.ContentPackage);
                        }
                        else
                        {
                            factionChanged = location.Faction != faction;
                            location.PreviousFaction = location.Faction;
                            location.Faction = faction;
                        }
                    }
                    if (!SecondaryFaction.IsEmpty)
                    {
                        var secondaryFaction = campaign.GetFaction(SecondaryFaction);
                        if (secondaryFaction == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a faction with the identifier \"{SecondaryFaction}\".",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                        }
                        else
                        {
                            factionChanged |= location.SecondaryFaction != secondaryFaction;
                            location.SecondaryFaction = secondaryFaction;
                        }
                    }
                    if (!Type.IsEmpty)
                    {
                        var locationType = LocationType.Prefabs.Find(lt => lt.Identifier == Type);
                        if (locationType == null)
                        {
                            DebugConsole.ThrowError($"Error in ModifyLocationAction ({ParentEvent.Prefab.Identifier}): could not find a location type with the identifier \"{Type}\".",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                        }
                        else if (!location.LocationTypeChangesBlocked)
                        {
                            typeChanged = location.Type != locationType;
                            // ChangeType resets the hires and notifies the clients of the new type before sending them the hires
                            location.ChangeType(campaign, locationType, createNetworkEvent: true);
                        }
                    }
                    if (!Name.IsEmpty)
                    {
                        location.ForceName(Name);
                    }
                    if (ResetKilledCharacters)
                    {
                        location.ResetKilledCharacters();
                    }
                    // If ChangeType was called it has already reset and sent the hires
                    bool resetHires = factionChanged && !typeChanged;
                    if (resetHires
                        // Clients receive the hires from the server. Stores are left as-is, the next world step updates their stock to match the new factions.
                        && GameMain.NetworkMember is not { IsClient: true })
                    {
                        location.ResetHireableCharacters();
                    }
                    if (ClearMissions)
                    {
                        location.ClearMissions();
                    }
#if SERVER
                    foreach (var client in GameMain.Server.ConnectedClients)
                    {
                        SendNetworkEvent(location, client);
                    }
                    // Send hires after the location update so that the factions are up to date
                    if (resetHires && location == campaign.CurrentLocation && campaign is MultiPlayerCampaign mpCampaign)
                    {
                        mpCampaign.SendCrewState(createNotification: false);
                    }
#endif
                }
            }
            isFinished = true;
        }

#if SERVER
        public static void SendNetworkEvent(Location location, Client client)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.MODIFYLOCATION);
            outmsg.WriteUInt16((UInt16)GameMain.GameSession.Map.Locations.IndexOf(location));
            outmsg.WriteIdentifier(location.Type.Identifier);
            outmsg.WriteIdentifier(location.Faction?.Prefab.Identifier ?? Identifier.Empty);
            outmsg.WriteIdentifier(location.SecondaryFaction?.Prefab.Identifier ?? Identifier.Empty);
            GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
        }
#endif

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ModifyLocationAction)}";
        }
    }
}