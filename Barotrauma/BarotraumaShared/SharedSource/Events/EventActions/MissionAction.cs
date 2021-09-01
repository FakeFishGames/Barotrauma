using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MissionAction : EventAction
    {
        [Serialize("", true)]
        public string MissionIdentifier { get; set; }

        [Serialize("", true)]
        public string MissionTag { get; set; }

        [Serialize("", true, description: "The type of the location the mission will be unlocked in (if empty, any location can be selected).")]
        public string LocationType { get; set; }

        [Serialize(0, true, description: "Minimum distance to the location the mission is unlocked in (1 = one path between locations).")]
        public int MinLocationDistance { get; set; }

        [Serialize(true, true, description: "If true, the mission has to be unlocked in a location further on the campaign map.")]
        public bool UnlockFurtherOnMap { get; set; }

        [Serialize(false, true, description: "If true, a suitable location is forced on the map if one isn't found.")]
        public bool CreateLocationIfNotFound { get; set; }

        private bool isFinished;

        public MissionAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            if (string.IsNullOrEmpty(MissionIdentifier) && string.IsNullOrEmpty(MissionTag))
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": neither MissionIdentifier or MissionTag has been configured.");
            }
            if (!string.IsNullOrEmpty(MissionIdentifier) && !string.IsNullOrEmpty(MissionTag))
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": both MissionIdentifier or MissionTag have been configured. The tag will be ignored.");
            }
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
                MissionPrefab prefab = null;
                var unlockLocation = FindUnlockLocation();
                if (unlockLocation == null && CreateLocationIfNotFound)
                {
                    //find an empty location at least 3 steps away, further on the map
                    var emptyLocation = FindUnlockLocationRecursive(campaign.Map.CurrentLocation, Math.Max(MinLocationDistance, 3), "none", true, new HashSet<Location>());
                    if (emptyLocation != null)
                    {
                        emptyLocation.ChangeType(Barotrauma.LocationType.List.Find(lt => lt.Identifier.Equals(LocationType, StringComparison.OrdinalIgnoreCase)));
                        unlockLocation = emptyLocation;
                    }
                }

                if (unlockLocation != null)
                {
                    if (!string.IsNullOrEmpty(MissionIdentifier))
                    {
                        prefab = unlockLocation.UnlockMissionByIdentifier(MissionIdentifier);                    
                    }
                    else if (!string.IsNullOrEmpty(MissionTag))
                    {
                        prefab = unlockLocation.UnlockMissionByTag(MissionTag);
                    }
                    if (campaign is MultiPlayerCampaign mpCampaign)
                    {
                        mpCampaign.LastUpdateID++;
                    }
                    if (prefab != null)
                    {
                        DebugConsole.NewMessage($"Unlocked mission \"{prefab.Name}\" in the location \"{unlockLocation.Name}\".");
    #if CLIENT
                        new GUIMessageBox(string.Empty, TextManager.GetWithVariable("missionunlocked", "[missionname]", prefab.Name), 
                            new string[0], type: GUIMessageBox.Type.InGame, icon: prefab.Icon, relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128))
                        {
                            IconColor = prefab.IconColor
                        };
    #else
                        NotifyMissionUnlock(prefab);
    #endif
                    }
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to find a suitable location to unlock a mission in (LocationType: {LocationType}, MinLocationDistance: {MinLocationDistance}, UnlockFurtherOnMap: {UnlockFurtherOnMap})");
                }
            }
            isFinished = true;
        }

        private Location FindUnlockLocation()
        {
            var campaign = GameMain.GameSession.GameMode as CampaignMode;
            if (string.IsNullOrEmpty(LocationType) && MinLocationDistance <= 1)
            {
                return campaign.Map.CurrentLocation;
            }

            return FindUnlockLocationRecursive(campaign.Map.CurrentLocation, 0, LocationType, UnlockFurtherOnMap, new HashSet<Location>());
        }

        private Location FindUnlockLocationRecursive(Location currLocation, int currDistance, string locationType, bool unlockFurtherOnMap, HashSet<Location> checkedLocations)
        {
            var campaign = GameMain.GameSession.GameMode as CampaignMode;
            if (currLocation.Type.Identifier.Equals(locationType, StringComparison.OrdinalIgnoreCase) && currDistance >= MinLocationDistance &&
                (!unlockFurtherOnMap || currLocation.MapPosition.X > campaign.Map.CurrentLocation.MapPosition.X))
            {
                return currLocation;
            }
            checkedLocations.Add(currLocation);
            foreach (LocationConnection connection in currLocation.Connections)
            {
                var otherLocation = connection.OtherLocation(currLocation);
                if (checkedLocations.Contains(otherLocation)) { continue; }
                var unlockLocation = FindUnlockLocationRecursive(otherLocation, ++currDistance, locationType, unlockFurtherOnMap, checkedLocations);
                if (unlockLocation != null) { return unlockLocation; }
            }
            return null;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionAction)} -> ({(string.IsNullOrEmpty(MissionIdentifier) ? MissionTag : MissionIdentifier)})";
        }
        
#if SERVER
        private void NotifyMissionUnlock(MissionPrefab prefab)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage outmsg = new WriteOnlyMessage();
                outmsg.Write((byte)ServerPacketHeader.EVENTACTION);
                outmsg.Write((byte)EventManager.NetworkEventType.MISSION);
                outmsg.Write(prefab.Identifier);
                GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
            }
        }
#endif
    }
}