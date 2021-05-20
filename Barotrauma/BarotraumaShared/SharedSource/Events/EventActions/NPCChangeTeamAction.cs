using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class NPCChangeTeamAction : EventAction
    {
        [Serialize("", true)]
        public string NPCTag { get; set; }

        [Serialize(0, true)]
        public int TeamTag { get; set; }

        [Serialize(false, true)]
        public bool AddToCrew { get; set; }

        private bool isFinished = false;

        public NPCChangeTeamAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private List<Character> affectedNpcs = null;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();
            foreach (var npc in affectedNpcs)
            {
                CharacterTeamType newTeam = (CharacterTeamType)TeamTag;
                // characters will still remain on friendlyNPC team for rest of the tick
                npc.SetOriginalTeam(newTeam);

                if (AddToCrew && (newTeam == CharacterTeamType.Team1 || newTeam == CharacterTeamType.Team2))
                {
                    npc.Info.StartItemsGiven = true;

                    GameMain.GameSession.CrewManager.AddCharacter(npc);
                    foreach (Item item in npc.Inventory.AllItems)
                    {
                        item.AllowStealing = true;
                        var wifiComponent = item.GetComponent<Items.Components.WifiComponent>();
                        if (wifiComponent != null)
                        {
                            wifiComponent.TeamID = newTeam;
                        }
                    }
#if SERVER
                    GameMain.NetworkMember.CreateEntityEvent(npc, new object[] { NetEntityEvent.Type.AddToCrew, newTeam, npc.Inventory.AllItems.Select(it => it.ID).ToArray() });
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