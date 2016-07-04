using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    class TraitorManager
    {
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
                if (!client.inGame || client.Character==null) continue;
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

            if (server.Character == null)
            {               
                new GUIMessageBox("New traitor", traitorCharacter.Name + " is the traitor and the target is " + targetCharacter.Name+".");
            }
            else if (server.Character == traitorCharacter)
            {
                CreateStartPopUp(traitorCharacter.Name);
                return;
            }

            server.NewTraitor(traitorCharacter, targetCharacter);
        }

        public static void CreateStartPopUp(string targetName)
        {
            new GUIMessageBox("You are the Traitor!",
                "Your secret task is to assassinate " + targetName + "! Discretion is an utmost concern; sinking the submarine and killing the entire crew "
                + "will arouse suspicion amongst the Fleet. If possible, make the death look like an accident.", 400, 350);
        }
        
        public string GetEndMessage()
        {
            if (GameMain.Server == null || traitorCharacter == null || targetCharacter == null) return "";

            string endMessage = "";

            if (targetCharacter.IsDead && !traitorCharacter.IsDead)
            {
                endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ". The task was successful.";
            }
            else if (targetCharacter.IsDead && traitorCharacter.IsDead)
            {
                endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ". The task was successful, but luckily the bastard didn't make it out alive either.";
            }
            else if (traitorCharacter.IsDead)
            {
                endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ", but ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                endMessage += " got " + ((traitorCharacter.Info.Gender == Gender.Male) ? "himself" : "herself");
                endMessage += " killed before completing it.";
            }
            else
            {
                endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ". ";
                endMessage += (Submarine.MainSub.AtEndPosition) ? 
                    "The task was unsuccessful - the has submarine reached its destination." : 
                    "The task was unsuccessful.";
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
