using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class TraitorMode : GameMode
    {
        private Character traitorCharacter, targetCharacter;

        public TraitorMode(GameModePreset preset)
            : base(preset)
        {

        }

        public override void Start()
        {
            base.Start();

            traitorCharacter = null;
            targetCharacter = null;
        }

        public override void Update(float deltaTime)
        {
            if (GameMain.Server == null) return;

            base.Update(deltaTime);

            if (!isRunning) return;


            if (traitorCharacter == null || targetCharacter == null)
            {
                GameMain.Server.NewTraitor(out traitorCharacter, out targetCharacter);
            }
            else
            {
                if (targetCharacter == null || targetCharacter.IsDead)
                {
                    string endMessage = traitorCharacter.Name + " was a traitor! ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + targetCharacter.Name + ". The task was successful.";
                    End(endMessage);
                }
                else if (traitorCharacter == null || traitorCharacter.IsDead)
                {
                    string endMessage = traitorCharacter.Name + " was a traitor! ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + targetCharacter.Name + ", but ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                    endMessage += " got " + ((traitorCharacter.Info.Gender == Gender.Male) ? "himself" : "herself");
                    endMessage += " killed before completing it.";
                    End(endMessage);
                    return;
                }
                else if (Submarine.Loaded.AtEndPosition)
                {
                    string endMessage = traitorCharacter.Name + " was a traitor! ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + targetCharacter.Name + ". ";
                    endMessage += "The task was unsuccessful - the has submarine reached its destination.";
                    End(endMessage);
                    return;
                }    

            }
        }

        public void CharacterLeft(Character character)
        {
            if (character != traitorCharacter && character != targetCharacter) return;

            if (character == traitorCharacter)
            {
                string endMessage = "The traitor has disconnected from the server.";
                End(endMessage);
            }
            else if (character == targetCharacter)
            {
                string endMessage = "The traitor's target has disconnected from the server.";
                End(endMessage);
            }
        }
    }
}
