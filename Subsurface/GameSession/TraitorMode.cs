using System;
using System.Linq;
using Subsurface.Networking;

namespace Subsurface
{
    class TraitorMode : GameMode
    {
        Client traitor;
        Client target;

        public TraitorMode(string name)
            : base(name)
        {

        }

        public override void Start(TimeSpan duration)
        {
            base.Start(duration);

            traitor = null;
            target = null;
        }

        public override void Update()
        {

            if (!isRunning) return;

            if (DateTime.Now >= endTime)
            {
                string endMessage = traitor.character.info.name + " was a traitor! ";
                endMessage += (traitor.character.info.gender == Gender.Male) ? "His" : "Her";
                endMessage += " task was to assassinate " + target.character.info.name + ". The task was unsuccesful.";
                End(endMessage);
                return;
            }
            

            if (traitor==null || target ==null)
            {
                int clientCount = Game1.server.connectedClients.Count();
                if (clientCount < 2) return;

                int traitorIndex = Game1.localRandom.Next(clientCount);
                traitor = Game1.server.connectedClients[traitorIndex];

                int targetIndex = 0;
                while (targetIndex==traitorIndex)
                {
                    targetIndex = Game1.localRandom.Next(clientCount);
                }
                target = Game1.server.connectedClients[targetIndex];


                Game1.server.NewTraitor(traitor, target);
            }
            else
            {
                if (target.character.IsDead)
                {
                    string endMessage = traitor.character.info.name + " was a traitor! ";
                    endMessage += (traitor.character.info.gender == Gender.Male) ? "his" : "her";
                    endMessage += " task was to assassinate " + target.character.info.name + ". The task was succesful.";
                    End(endMessage);
                }
            }
        }
    }
}
