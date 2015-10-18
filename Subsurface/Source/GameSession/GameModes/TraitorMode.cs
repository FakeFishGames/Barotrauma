using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class TraitorMode : GameMode
    {
        Client traitor;
        Client target;

        private Character traitorCharacter, targetCharacter;

        public TraitorMode(GameModePreset preset)
            : base(preset)
        {

        }

        public override void Start()
        {
            base.Start();

            traitor = null;
            target = null;
        }

        public override void Update(float deltaTime)
        {
            if (GameMain.Server == null) return;

            base.Update(deltaTime);

            if (!isRunning) return;
       

            if (traitor==null || target ==null)
            {
                int clientCount = GameMain.Server.connectedClients.Count();
                if (clientCount < 2) return;

                int traitorIndex = Rand.Int(clientCount, false);
                traitor = GameMain.Server.connectedClients[traitorIndex];

                int targetIndex = 0;
                while (targetIndex == traitorIndex)
                {
                    targetIndex = Rand.Int(clientCount, false);
                }
                target = GameMain.Server.connectedClients[targetIndex];

                traitorCharacter = traitor.character;
                targetCharacter = target.character;


                GameMain.Server.NewTraitor(traitor, target);
            }
            else
            {
                if (target.character == null || target.character.IsDead)
                {
                    string endMessage = traitorCharacter.Name + " was a traitor! ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + targetCharacter.Name + ". The task was successful.";
                    End(endMessage);
                }
                else if (traitor.character == null || traitor.character.IsDead)
                {
                    string endMessage = traitorCharacter.Name + " was a traitor! ";
                    //TODO: remove references to traitor.character
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + targetCharacter.Name + ", but ";
                    endMessage += (traitorCharacter.Info.Gender == Gender.Male) ? "he" : "she";
                    endMessage += " got " + ((traitorCharacter.Info.Gender == Gender.Male) ? "himself" : "herself");
                    endMessage += " killed before completing it.";
                    End(endMessage);
                    return;
                }
                else if (Level.Loaded.AtEndPosition)
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
    }
}
