using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class MissionAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier MissionIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier MissionTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier RequiredFaction { get; set; }

        public ImmutableArray<Identifier> LocationTypes { get; }

        [Serialize(0, IsPropertySaveable.Yes, description: "Minimum distance to the location the mission is unlocked in (1 = one path between locations).")]
        public int MinLocationDistance { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, the mission has to be unlocked in a location further on the campaign map.")]
        public bool UnlockFurtherOnMap { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, a suitable location is forced on the map if one isn't found.")]
        public bool CreateLocationIfNotFound { get; set; }

        private bool isFinished;

        public MissionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (MissionIdentifier.IsEmpty && MissionTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": neither MissionIdentifier or MissionTag has been configured.");
            }
            if (!MissionIdentifier.IsEmpty && !MissionTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": both MissionIdentifier or MissionTag have been configured. The tag will be ignored.");
            }
            LocationTypes = element.GetAttributeIdentifierArray("locationtype", Array.Empty<Identifier>()).ToImmutableArray();
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                Mission unlockedMission = null;
                var unlockLocation = FindUnlockLocation(MinLocationDistance, UnlockFurtherOnMap, LocationTypes);
                if (unlockLocation == null && CreateLocationIfNotFound)
                {
                    //find an empty location at least 3 steps away, further on the map
                    var emptyLocation = FindUnlockLocation(Math.Max(MinLocationDistance, 3), unlockFurtherOnMap: true, "none".ToIdentifier().ToEnumerable());
                    if (emptyLocation != null)
                    {
                        emptyLocation.ChangeType(campaign, Barotrauma.LocationType.Prefabs[LocationTypes[0]]);
                        unlockLocation = emptyLocation;
                    }
                }

                if (unlockLocation != null)
                {
                    if (!MissionIdentifier.IsEmpty)
                    {
                        unlockedMission = unlockLocation.UnlockMissionByIdentifier(MissionIdentifier);                    
                    }
                    else if (!MissionTag.IsEmpty)
                    {
                        unlockedMission = unlockLocation.UnlockMissionByTag(MissionTag);
                    }
                    if (campaign is MultiPlayerCampaign mpCampaign)
                    {
                        mpCampaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
                    }
                    if (unlockedMission != null)
                    {
                        campaign.Map.Discover(unlockLocation, checkTalents: false);
                        if (unlockedMission.Locations[0] == unlockedMission.Locations[1] || unlockedMission.Locations[1] ==null)
                        {
                            DebugConsole.NewMessage($"Unlocked mission \"{unlockedMission.Name}\" in the location \"{unlockLocation.Name}\".");
                        }
                        else
                        {
                            DebugConsole.NewMessage($"Unlocked mission \"{unlockedMission.Name}\" in the connection from \"{unlockedMission.Locations[0].Name}\" to \"{unlockedMission.Locations[1].Name}\".");
                        }
#if CLIENT
                        new GUIMessageBox(string.Empty, TextManager.GetWithVariable("missionunlocked", "[missionname]", unlockedMission.Name), 
                            Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: unlockedMission.Prefab.Icon, relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128))
                        {
                            IconColor = unlockedMission.Prefab.IconColor
                        };
#else
                        NotifyMissionUnlock(unlockedMission, unlockLocation);
#endif
                    }
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to find a suitable location to unlock a mission in (LocationType: {LocationTypes}, MinLocationDistance: {MinLocationDistance}, UnlockFurtherOnMap: {UnlockFurtherOnMap})");
                }
            }
            isFinished = true;
        }

        private Location FindUnlockLocation(int minDistance, bool unlockFurtherOnMap, IEnumerable<Identifier> locationTypes)
        {
            var campaign = GameMain.GameSession.GameMode as CampaignMode;
            if (LocationTypes.Length == 0 && minDistance <= 1)
            {
                return campaign.Map.CurrentLocation;
            }

            var currentLocation = campaign.Map.CurrentLocation;
            int distance = 0;
            HashSet<Location> checkedLocations = new HashSet<Location>();
            HashSet<Location> pendingLocations = new HashSet<Location>() { currentLocation };
            do
            {
                List<Location> currentLocations = pendingLocations.ToList();
                pendingLocations.Clear();
                foreach (var location in currentLocations)
                {
                    checkedLocations.Add(location);
                    if (IsLocationValid(currentLocation, location, unlockFurtherOnMap, distance, minDistance, locationTypes)) 
                    {
                        return location;
                    }
                    else
                    {
                        foreach (LocationConnection connection in location.Connections)
                        {
                            var otherLocation = connection.OtherLocation(location);
                            if (checkedLocations.Contains(otherLocation)) { continue; }
                            pendingLocations.Add(otherLocation);
                        }
                    }
                }
                distance++;
            } while (pendingLocations.Any());

            return null;
        }

        private bool IsLocationValid(Location currLocation, Location location, bool unlockFurtherOnMap, int distance, int minDistance, IEnumerable<Identifier> locationTypes)
        {
            if (!RequiredFaction.IsEmpty)
            {
                if (location.Faction?.Prefab.Identifier != RequiredFaction &&
                    location.SecondaryFaction?.Prefab.Identifier != RequiredFaction)
                {
                    return false;
                }
            }
            if (!locationTypes.Contains(location.Type.Identifier) && !(location.HasOutpost() && locationTypes.Contains("AnyOutpost".ToIdentifier())))
            {
                return false;
            }
            if (distance < minDistance) 
            { 
                return false; 
            }
            if (unlockFurtherOnMap && location.MapPosition.X < currLocation.MapPosition.X)
            {
                return false;
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionAction)} -> ({(MissionIdentifier.IsEmpty ? MissionTag : MissionIdentifier)})";
        }

#if SERVER
        private static void NotifyMissionUnlock(Mission mission, Location unlockLocation)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage outmsg = new WriteOnlyMessage();
                outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
                outmsg.WriteByte((byte)EventManager.NetworkEventType.MISSION);
                outmsg.WriteIdentifier(mission.Prefab.Identifier);
                outmsg.WriteInt32(GameMain.GameSession?.Map?.Locations.IndexOf(unlockLocation) ?? -1);
                outmsg.WriteString(mission.Name.Value);
                GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
            }
        }
#endif
    }
}