using System;
using System.Linq;
using Subsurface.Networking;

namespace Subsurface
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
            base.Update(deltaTime);

            if (!isRunning) return;

            if (DateTime.Now >= endTime)
            {
                string endMessage = traitor.character.Info.Name + " was a traitor! ";
                endMessage += (traitor.character.Info.Gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + target.character.Info.Name + ". The task was unsuccesful.";
                End(endMessage);
                return;
            }
            

            if (traitor==null || target ==null)
            {
                int clientCount = Game1.Server.connectedClients.Count();
                if (clientCount < 2) return;

                int traitorIndex = Rand.Int(clientCount, false);
                traitor = Game1.Server.connectedClients[traitorIndex];

                int targetIndex = 0;
                while (targetIndex == traitorIndex)
                {
                    targetIndex = Rand.Int(clientCount, false);
                }
                target = Game1.Server.connectedClients[targetIndex];


                Game1.Server.NewTraitor(traitor, target);
            }
            else
            {
                if (target.character.IsDead)
                {
                    string endMessage = traitor.character.Info.Name + " was a traitor! ";
                    endMessage += (traitor.character.Info.Gender == Gender.Male) ? "his" : "her";
                    endMessage += " task was to assassinate " + target.character.Info.Name + ". The task was succesful.";
                    End(endMessage);
                }
            }
        }
    }
}
