#define ALLOW_LONE_TRAITOR // Set this to allow traitor in games with only one connected player, missions to kill someone will target that same player.

using Barotrauma.Networking;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class Traitor
    {
        public readonly Character Character;

        public enum TaskType // Might not be needed..
        {
            // initial
            DestroyCargo, // Destroy jettisoned cargo x distance from sub
            JamSonar,
            SabotageBallastPumps, // duration
            SabotageOxygenGenerator, // duration
            SabotageAllDivingSuitsAndMasks,
            DestroyAllNonInventoryOxygenTanksOnSub,
            CovertlyInfectSpecificMemberWithHusk,
            SabotageAllCoilOrRailGunAmmo,
            SabotageEngine, // duration
            // additional goals, all within x seconds
            KillCrewMember, // without being seen
            KillAllCrew,
            CauseMeltdown,
            FloodPercentOfSub,
            DetonateLocations // SetExplosivesAtSpecificLocationsAndDetonateSimultaneously
        }

        public class Task
        {
            // TODO(xxx): Task server messages interface needed? Or can we use generic serialization to handle list of tasks?
            public readonly Traitor Traitor;

            public virtual string StartMessage => "(start message not implemented)";
            public virtual string EndMessage => "(end message not implemented)";

            public virtual string DebugInfo => "(debug info not implemented");

            protected Task()
            {
                throw new System.Exception("Attempt to instantiate Task with no implementation.");
            }

            protected Task(Traitor traitor)
            {
                Traitor = traitor;
            }
        }

        public class TaskDestroyCargo : Task
        {
            public float DistanceFromSub;
        }

        public class TaskJamSonar : Task
        {
        }

        public class TaskSabotagePumps : Task
        {
            public float Duration;
        }

        public class TaskSabotageOxygenGenerator : Task
        {
            public float Duration;
        }

        public class TaskSabotageDivingGear : Task
        {
        }

        public class TaskDestroyOxygenTanks : Task
        {
        }

        public class TaskInfectWithHusk : Task
        {
            public Character Target;
        }

        public class TaskSabotageAmmo : Task
        {
        }

        public class TaskSabotageEngine : Task
        {
            public float Duration;
        }

        public class TaskKillTarget : Task
        {
            public readonly Character Target;

            public override string StartMessage => TextManager.GetWithVariable("TraitorStartMessage", "[targetname]", Target.Name);
            public override string EndMessage
            {
                get
                {
                    var traitorCharacter = Traitor.Character;
                    var targetCharacter = Target;
                    string messageTag;
                    if (targetCharacter.IsDead) //Partial or complete mission success
                    {
                        if (traitorCharacter.IsDead)
                        {
                            messageTag = "TraitorEndMessageSuccessTraitorDead";
                        }
                        else if (traitorCharacter.LockHands)
                        {
                            messageTag = "TraitorEndMessageSuccessTraitorDetained";
                        }
                        else
                            messageTag = "TraitorEndMessageSuccess";
                    }
                    else //Partial or complete failure
                    {
                        if (traitorCharacter.IsDead)
                        {
                            messageTag = "TraitorEndMessageFailureTraitorDead";
                        }
                        else if (traitorCharacter.LockHands)
                        {
                            messageTag = "TraitorEndMessageFailureTraitorDetained";
                        }
                        else
                        {
                            messageTag = "TraitorEndMessageFailure";
                        }
                    }

                    return (TextManager.ReplaceGenderPronouns(TextManager.GetWithVariables(messageTag, new string[2] { "[traitorname]", "[targetname]" },
                        new string[2] { traitorCharacter.Name, targetCharacter.Name }), traitorCharacter.Info.Gender) + "\n");
                }
            }

            public override string DebugInfo => string.Format("Kill targett {0}.", Target.Name);

            public TaskKillTarget(Traitor traitor, Character target): base(traitor)
            {
                Target = target;
            }
        }

        public class TaskKillAllCrew : Task
        {
        }

        public class TaskCauseMeltdown : Task
        {
        }

        public class TaskFloodPercentOfSub : Task
        {
            public float RequiredPercent;
        }

        public class DetonateLocations : Task
        {
            public class Location { } // TODO(xxx): Best way to identify target locations?
            public readonly List<Location> Locations = new List<Location>();
        }


        public Task CurrentTask;

        public Character TargetCharacter; //TODO(xxx): make a modular objective system (similar to crew missions) that allows for things OTHER than assasinations.

        public Traitor(Character character)
        {
            Character = character;
        }

        public void Greet(GameServer server, string codeWords, string codeResponse)
        {
            string greetingMessage = TextManager.GetWithVariable("TraitorStartMessage", "[targetname]", TargetCharacter.Name);
            string moreAgentsMessage = TextManager.GetWithVariables("TraitorMoreAgentsMessage",
                new string[2] { "[codewords]", "[coderesponse]" }, new string[2] { codeWords, codeResponse });
            
            var greetingChatMsg = ChatMessage.Create(null, greetingMessage, ChatMessageType.Server, null);
            var moreAgentsChatMsg = ChatMessage.Create(null, moreAgentsMessage, ChatMessageType.Server, null);

            var moreAgentsMsgBox = ChatMessage.Create(null, moreAgentsMessage, ChatMessageType.MessageBox, null);
            var greetingMsgBox = ChatMessage.Create(null, greetingMessage, ChatMessageType.MessageBox, null);

            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendDirectChatMessage(greetingChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(moreAgentsChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(greetingMsgBox, traitorClient);
            GameMain.Server.SendDirectChatMessage(moreAgentsMsgBox, traitorClient);

            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                var ownerMsg = ChatMessage.Create(
                    null,//TextManager.Get("NewTraitor"),
                    TextManager.GetWithVariables("TraitorStartMessageServer", new string[2] { "[targetname]", "[traitorname]" }, new string[2] { TargetCharacter.Name, Character.Name }),
                    ChatMessageType.MessageBox,
                    null
                );
                GameMain.Server.SendDirectChatMessage(ownerMsg, ownerClient);
            }
        }
    }

    partial class TraitorManager
    {
        private static string wordsTxt = Path.Combine("Content", "CodeWords.txt");

        public List<Traitor> TraitorList
        {
            get { return traitorList; }
        }

        private List<Traitor> traitorList = new List<Traitor>();

        public string codeWords, codeResponse;

        public TraitorManager(GameServer server, int traitorCount)
        {
            if (traitorCount < 1) //what why how
            {
                traitorCount = 1;
                DebugConsole.ThrowError("Traitor Manager: TraitorCount somehow ended up less than 1, setting it to 1.");
            }
            Start(server, traitorCount);
        }

        private void Start(GameServer server, int traitorCount)
        {
            if (server == null) return;

            List<Character> characters = new List<Character>(); //ANYONE can be a target.
            List<Character> traitorCandidates = new List<Character>(); //Keep this to not re-pick traitors twice
            foreach (Client client in server.ConnectedClients)
            {
                if (client.Character != null)
                {
                    characters.Add(client.Character);
                    traitorCandidates.Add(client.Character);
                }
            }

            if (server.Character != null)
            {
                characters.Add(server.Character); //Add host character
                traitorCandidates.Add(server.Character);
            }
#if !ALLOW_LONE_TRAITOR
            if (characters.Count < 2)
            {
                return;
            }
#endif
            codeWords = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
            codeResponse = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);

            while (traitorCount-- > 0)
            {
                if (traitorCandidates.Count <= 0) break;

                int traitorIndex = Rand.Int(traitorCandidates.Count);
                Character traitorCharacter = traitorCandidates[traitorIndex];
                traitorCandidates.Remove(traitorCharacter);

                //Add them to the list
                traitorList.Add(new Traitor(traitorCharacter));
            }

            //Now that traitors have been decided, let's do objectives in post for deciding things like Document Exchange.
            foreach (Traitor traitor in traitorList)
            {
                Character traitorCharacter = traitor.Character;
                int targetIndex = Rand.Int(characters.Count);
#if !ALLOW_LONE_TRAITOR
                while (characters[targetIndex] == traitorCharacter) //Cannot target self
                {
                    targetIndex = Rand.Int(characters.Count);
                }
#endif
                Character targetCharacter = characters[targetIndex];
                traitor.TargetCharacter = targetCharacter;
                traitor.Greet(server, codeWords, codeResponse);
            }
        }

        // <xxx>
        public void CargoDestroyed()
        {
        }

        Dictionary<System.Type, System.Action<Barotrauma.Items.Components.ItemComponent>> sabotageItemHandlers = new Dictionary<System.Type, System.Action<Barotrauma.Items.Components.ItemComponent>> {
            {
                typeof(Barotrauma.Items.Components.Sonar), (sonar) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage sonar");
                }
            },
            {
                typeof(Barotrauma.Items.Components.Pump), (pump) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage pump");
                }
            },
            {
                typeof(Barotrauma.Items.Components.Reactor), (reactor) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage reactor");
                }
            }/*,
            {
                typeof(Barotrauma.Items.Components.Mask//
            }*/

        };

        public void SabotageItem(Barotrauma.Item item)
        {
            // TODO: Best way of recognizing items to sabotage? We also need to maintain an item count for each type we're interested in.
            if (item.Tags.Contains("oxygensource"))
            {
            }

            foreach (var component in item.Components) {
                if (sabotageItemHandlers.TryGetValue(component.GetType(), out var handler))
                {
                    handler(component);
                }
            }
        }
    
        // SabotageAllDivingSuitsAndMasks,
    /*
            DestroyAllNonInventoryOxygenTanksOnSub,
            CovertlyInfectSpecificMemberWithHusk,
            SabotageAllCoilOrRailGunAmmo,
            SabotageEngine // duration
            // additional goals, all within x seconds
            KillCrewMemberWithoutBeingSeenToDoSo,
            KillAllCrew,
            CauseMeltdown,
            FloodPercentOfSub,
            SetExplosivesAtSpecificLocationsAndDetonateSimultaneously
            */

        // </xxx>

        public string GetEndMessage()
        {
            if (GameMain.Server == null || traitorList.Count <= 0) return "";

            string endMessage = "";

            foreach (Traitor traitor in traitorList)
            {
                Character traitorCharacter = traitor.Character;
                Character targetCharacter = traitor.TargetCharacter;
                string messageTag;

                // TODO(xxx): Mission completion criteria here
                if (targetCharacter.IsDead) //Partial or complete mission success
                {
                    if (traitorCharacter.IsDead)
                    {
                        messageTag = "TraitorEndMessageSuccessTraitorDead";
                    }
                    else if (traitorCharacter.LockHands)
                    {
                        messageTag = "TraitorEndMessageSuccessTraitorDetained";
                    }
                    else
                        messageTag = "TraitorEndMessageSuccess";
                }
                else //Partial or complete failure
                {
                    if (traitorCharacter.IsDead)
                    {
                        messageTag = "TraitorEndMessageFailureTraitorDead";
                    }
                    else if (traitorCharacter.LockHands)
                    {
                        messageTag = "TraitorEndMessageFailureTraitorDetained";
                    }
                    else
                    {
                        messageTag = "TraitorEndMessageFailure";
                    }
                }

                endMessage += (TextManager.ReplaceGenderPronouns(TextManager.GetWithVariables(messageTag, new string[2] { "[traitorname]", "[targetname]" },
                    new string[2] { traitorCharacter.Name, targetCharacter.Name }), traitorCharacter.Info.Gender) + "\n");
            }

            return endMessage;
        }
    }
}
