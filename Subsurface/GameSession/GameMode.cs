using System;
using System.Collections.Generic;

namespace Subsurface
{
    class GameMode
    {
        public static List<GameMode> list = new List<GameMode>();

        //TimeSpan duration;
        protected DateTime startTime;
        protected DateTime endTime;

        protected bool isRunning;

        protected string name;

        private string endMessage;

        public DateTime StartTime
        {
            get { return startTime; }
        }

        public DateTime EndTime
        {
            get { return endTime; }
        }

        public bool IsRunning
        {
            get { return isRunning; }
        }

        public string Name
        {
            get { return name; }
        }

        public string EndMessage
        {
            get { return endMessage; }
        }

        public GameMode(string name)
        {
            this.name = name;

            list.Add(this);
        }

        public virtual void Start(TimeSpan duration)
        {
            startTime = DateTime.Now;
            endTime = startTime + duration;

            endMessage = "The round has ended!";

            isRunning = true;
        }

        public virtual void Update()
        {
            if (!isRunning) return;

            if (DateTime.Now >= endTime)
            {
                End(endMessage);                
            }
        }

        public void End(string endMessage = "")
        {
            isRunning = false;

            if (endMessage != "" || this.endMessage == null) this.endMessage = endMessage;

            Game1.gameSession.EndShift(null, null);
        }

        public static void Init()
        {
            new GameMode("Sandbox");
            new TraitorMode("Traitor");
        }
    }
}
