using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCChangeTeamAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier NPCTag { get; set; }

        [Serialize(CharacterTeamType.None, IsPropertySaveable.Yes)]
        public CharacterTeamType TeamTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AddToCrew { get; set; }

        private bool isFinished = false;

        public NPCChangeTeamAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private List<Character> affectedNpcs = null;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();
            foreach (var npc in affectedNpcs)
            {
                // characters will still remain on friendlyNPC team for rest of the tick
                npc.SetOriginalTeam(TeamTag);

                if (AddToCrew && (TeamTag == CharacterTeamType.Team1 || TeamTag == CharacterTeamType.Team2))
                {
                    npc.Info.StartItemsGiven = true;

                    GameMain.GameSession.CrewManager.AddCharacter(npc);
                    foreach (Item item in npc.Inventory.AllItems)
                    {
                        item.AllowStealing = true;
                        var wifiComponent = item.GetComponent<Items.Components.WifiComponent>();
                        if (wifiComponent != null)
                        {
                            wifiComponent.TeamID = TeamTag;
                        }
                        var idCard = item.GetComponent<Items.Components.IdCard>();
                        if (idCard != null)
                        {
                            idCard.TeamID = TeamTag;
                            idCard.SubmarineSpecificID = 0;
                        }
                    }

                    WayPoint subWaypoint = 
                        WayPoint.WayPointList.Find(wp => wp.Submarine == Submarine.MainSub && wp.SpawnType == SpawnType.Human && wp.AssignedJob == npc.Info.Job?.Prefab) ??
                        WayPoint.WayPointList.Find(wp => wp.Submarine == Submarine.MainSub && wp.SpawnType == SpawnType.Human);
                    if (subWaypoint != null)
                    {
                        npc.GiveIdCardTags(subWaypoint, createNetworkEvent: true);
                    }
#if SERVER
                    GameMain.NetworkMember.CreateEntityEvent(npc, new Character.AddToCrewEventData(TeamTag, npc.Inventory.AllItems));
#endif
                }
            }
            isFinished = true;
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(NPCChangeTeamAction)} -> (NPCTag: {NPCTag.ColorizeObject()})";
        }
    }
}