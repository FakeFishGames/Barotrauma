using Barotrauma.Networking;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class Traitor
    {
        public Character Character;
        public Character TargetCharacter;

        public Traitor(Character character, Character targetCharacter)
        {
            Character = character; TargetCharacter = targetCharacter;
        }
    }

    partial class TraitorManager
    {
        private static string CodeWords = Path.Combine("Content", "CodeWords.txt");

        public List<Traitor> TraitorList
        {
            get { return traitorList; }
        }

        private List<Traitor> traitorList;

        public TraitorManager(GameServer server, int TraitorCount)
        {
            if(TraitorCount < 1) //what why how
            {
                TraitorCount = 1;
                DebugConsole.ThrowError("Traitor Manager: TraitorCount somehow ended up less than 1, setting it to 1.");
            }
            Start(server, TraitorCount);
        }

        private void Start(GameServer server, int TraitorCount)
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

            if (characters.Count < 2)
            {
                return;
            }

            string codeWords = ToolBox.GetRandomLine(CodeWords) + ", " + ToolBox.GetRandomLine(CodeWords);
            string codeResponse = ToolBox.GetRandomLine(CodeWords) + ", " + ToolBox.GetRandomLine(CodeWords);

            traitorList = new List<Traitor>();
            while (TraitorCount-- >= 0)
            {
                if (traitorCandidates.Count <= 0)
                    break;

                int traitorIndex = Rand.Int(traitorCandidates.Count);
                var traitorCharacter = traitorCandidates[traitorIndex];
                traitorCandidates.Remove(traitorCharacter);

                int targetIndex = Rand.Int(characters.Count);
                while (characters[targetIndex] == traitorCharacter) //Cannot target self
                {
                    targetIndex = Rand.Int(characters.Count);
                }
                var targetCharacter = characters[targetIndex];

                //Add them to the list
                traitorList.Add(new Traitor(traitorCharacter, targetCharacter));

                string greetingMessage = "You are the Traitor! Your secret task is to assassinate " + targetCharacter.Name + "! Discretion is an utmost concern; sinking the submarine and killing the entire crew "
                + "will arouse suspicion amongst the Fleet. If possible, make the death look like an accident.";
                string moreAgentsMessage = "It is possible that there are other agents on this submarine. You don't know their names, but you do have a method of communication. "
                + "Use the code words to greet the agent and code response to respond. Disguise such words in a normal-looking phrase so the crew doesn't suspect anything.";
                moreAgentsMessage += "\nThe code words are: " + codeWords + ".";
                moreAgentsMessage += "\nThe code response is: " + codeResponse + ".";

                if (server.Character != traitorCharacter)
                {
                    var chatMsg = ChatMessage.Create(
                    null,
                    greetingMessage + "\n" + moreAgentsMessage,
                    (ChatMessageType)ChatMessageType.Server,
                    null);

                    var msgBox = ChatMessage.Create(
                    null,
                    "There might be other agents. Use these to communicate with them." +
                    "\nThe code words are: " + codeWords + "." +
                    "\nThe code response is: " + codeResponse + ".",
                    (ChatMessageType)ChatMessageType.MessageBox,
                    null);

                    Client client = server.ConnectedClients.Find(c => c.Character == traitorCharacter);
                    GameMain.Server.SendChatMessage(chatMsg, client);
                    GameMain.Server.SendChatMessage(msgBox, client);
                }

#if CLIENT
                if (server.Character == null)
                {
                    new GUIMessageBox("New traitor", traitorCharacter.Name + " is the traitor and the target is " + targetCharacter.Name+".");
                }
                else if (server.Character == traitorCharacter)
                {
                    CreateStartPopUp(targetCharacter.Name);
                    GameMain.NetworkMember.AddChatMessage(greetingMessage + "\n" + moreAgentsMessage, ChatMessageType.Server);
                    GameMain.NetworkMember.AddChatMessage("There might be other agents. Use these to communicate with them." +
                    "\nThe code words are: " + codeWords + "." +
                    "\nThe code response is: " + codeResponse + ".", ChatMessageType.MessageBox);
                    return;
                }
#endif
            }
        }

        public string GetEndMessage()
        {
            if (GameMain.Server == null || traitorList.Count <= 0) return "";

            string endMessage = "";

            foreach (Traitor traitor in traitorList)
            {
                Character traitorCharacter = traitor.Character;
                Character targetCharacter = traitor.TargetCharacter;
                endMessage += traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name;

                if (targetCharacter.IsDead) //Partial or complete mission success
                {
                    endMessage += ". The task was successful";
                    if (traitorCharacter.IsDead)
                    {
                        endMessage += ", but luckily the bastard didn't make it out alive either.";
                    }
                    else if (traitorCharacter.LockHands)
                    {
                        endMessage += ", but ";
                        endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                        endMessage += " was successfuly detained.";
                    }
                    else
                        endMessage += ".";
                }
                else //Partial or complete failure
                {
                    if (traitorCharacter.IsDead)
                    {
                        endMessage += ", but ";
                        endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                        endMessage += " got " + ((traitorCharacter.Info.Gender == Gender.Male) ? "himself" : "herself");
                        endMessage += " killed before completing it.";
                    }
                    else
                    {
                        endMessage += ". The task was unsuccessful";
                        if (traitorCharacter.LockHands)
                        {
                            endMessage += " - ";
                            endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                            endMessage += " was successfuly detained";
                        }
                        if (Submarine.MainSub.AtEndPosition)
                        {
                            endMessage += (traitorCharacter.LockHands ? " and " : " - ");
                            endMessage += "the submarine has reached its destination";
                        }
                        endMessage += ".";
                    }
                }
                endMessage += "\n";
            }

            return endMessage;          
        }

        //public void CharacterLeft(Character character)
        //{
        //    if (character != traitorCharacter && character != targetCharacter) return;

        //    if (character == traitorCharacter)
        //    {
        //        string endMessage = "The traitor has disconnected from the server.";
        //        End(endMessage);
        //    }
        //    else if (character == targetCharacter)
        //    {
        //        string endMessage = "The traitor's target has disconnected from the server.";
        //        End(endMessage);
        //    }
        //}
    }
}
