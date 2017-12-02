using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class TraitorManager
    {
        public Character TraitorCharacter
        {
            get { return traitorCharacter; }
        }

        public Character TargetCharacter
        {
            get { return targetCharacter; }
        }

        private Character traitorCharacter, targetCharacter;
        
        public TraitorManager(GameServer server)
        {
            Start(server);
        }

        private void Start(GameServer server)
        {
            if (server == null) return;

            List<Character> characters = new List<Character>();
            foreach (Client client in server.ConnectedClients)
            {
                if (client.Character != null)
                    characters.Add(client.Character);
            }

            if (server.Character!= null) characters.Add(server.Character);

            if (characters.Count < 2)
            {
                traitorCharacter = null;
                targetCharacter = null;
                return;
            }

            int traitorIndex = Rand.Range(0, characters.Count);

            int targetIndex = Rand.Range(0, characters.Count);
            while (targetIndex == traitorIndex)
            {
                targetIndex = Rand.Range(0, characters.Count);
            }

            traitorCharacter = characters[traitorIndex];
            targetCharacter = characters[targetIndex];

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

        public string GetEndMessage()
        {
            if (GameMain.Server == null || traitorCharacter == null || targetCharacter == null) return "";

            string endMessage = "";

            endMessage = traitorCharacter.Name + " was a traitor! ";
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
