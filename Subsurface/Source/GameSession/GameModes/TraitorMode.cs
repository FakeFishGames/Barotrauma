using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class TraitorManager
    {
        private Character traitorCharacter, targetCharacter;

        public TraitorManager(GameServer server)
        {
            server.NewTraitor(out traitorCharacter, out targetCharacter);
        }
        
        public string GetEndMessage()
        {
            if (GameMain.Server == null || traitorCharacter == null || targetCharacter == null) return "";

            if (targetCharacter.IsDead)
            {
                string endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ". The task was successful.";
                //End(endMessage);
            }
            else if (traitorCharacter.IsDead)
            {
                string endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ", but ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                endMessage += " got " + ((traitorCharacter.Info.Gender == Gender.Male) ? "himself" : "herself");
                endMessage += " killed before completing it.";

                return endMessage;
            }
            else
            {
                string endMessage = traitorCharacter.Name + " was a traitor! ";
                endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + targetCharacter.Name + ". ";
                endMessage += (Submarine.Loaded.AtEndPosition) ? 
                    "The task was unsuccessful - the has submarine reached its destination." : 
                    "The task was unsuccessful.";

                return endMessage;
            }

            return "";

            
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
