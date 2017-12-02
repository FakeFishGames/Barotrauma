using Barotrauma.Networking;
using System.Collections.Generic;

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
            
            if (server.Character!= null) characters.Add(server.Character); //Add host character

            if (characters.Count < 2)
            {
                return;
            }

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

#if CLIENT
                if (server.Character == null)
                {
                    new GUIMessageBox("New traitor", traitorCharacter.Name + " is the traitor and the target is " + targetCharacter.Name+".");
                }
                else if (server.Character == traitorCharacter)
                {
                    CreateStartPopUp(targetCharacter.Name);
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
