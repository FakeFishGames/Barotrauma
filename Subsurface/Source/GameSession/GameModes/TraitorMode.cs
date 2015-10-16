using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class TraitorMode : GameMode
    {
        Client traitor;
        Client target;

        public TraitorMode(GameModePreset preset)
            : base(preset)
        {

        }

        public override void Start(TimeSpan duration)
        {
            base.Start(duration);

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


                GameMain.Server.NewTraitor(traitor, target);
            }
            else
            {
                if (target.character.IsDead)
                {
                    string endMessage = traitor.character.Info.Name + " was a traitor! ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + target.character.Info.Name + ". The task was succesful.";
                    End(endMessage);
                }
                else if (traitor.character.IsDead)
                {
                    string endMessage = traitor.character.Info.Name + " was a traitor! ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + target.character.Info.Name + ", but ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "he" : "she";
                    endMessage += " got " + ((traitor.character.Info.Gender == Gender.Male) ? "himself" : "herself");
                    endMessage += " killed before completing it.";
                    End(endMessage);
                    return;
                }
                else if (Level.Loaded.AtEndPosition)
                {
                    string endMessage = traitor.character.Info.Name + " was a traitor! ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + target.character.Info.Name + ". ";
                    endMessage += "The task was unsuccessful - the has submarine reached its destination.";
                    End(endMessage);
                    return;
                }
                else if (DateTime.Now >= endTime)
                {
                    string endMessage = traitor.character.Info.Name + " was a traitor! ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "His" : "Her";
                    endMessage += " task was to assassinate " + target.character.Info.Name + ". The task was unsuccesful.";
                    End(endMessage);
                    return;
                }    

            }
        }
    }
}
