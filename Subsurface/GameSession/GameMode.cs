using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Subsurface
{
    class GameModePreset
    {
        public static List<GameModePreset> list = new List<GameModePreset>();

        public ConstructorInfo Constructor;
        public string Name;
        public bool IsSinglePlayer;

        public GameModePreset(string name, Type type, bool isSinglePlayer = false)
        {
            this.Name = name;
            //Constructor = constructor;


            Constructor = type.GetConstructor(new Type[] { typeof(GameModePreset) });

            IsSinglePlayer = isSinglePlayer;

            list.Add(this);
        }

        public GameMode Instantiate()
        {
            object[] lobject = new object[] { this };
            return(GameMode)Constructor.Invoke(lobject);            
        }
    }

    class GameMode
    {
        public static List<GameModePreset> presetList = new List<GameModePreset>();

        TimeSpan duration;
        protected DateTime startTime;
        protected DateTime endTime;

        //public readonly bool IsSinglePlayer;

        private GUIProgressBar timerBar;




        protected bool isRunning;

        //protected string name;

        protected GameModePreset preset;

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

        public bool IsSinglePlayer
        {
            get { return preset.IsSinglePlayer; }
        }

        public string Name
        {
            get { return preset.Name; }
        }

        public string EndMessage
        {
            get { return endMessage; }
        }

        public GameMode(GameModePreset preset)
        {
            this.preset = preset;


            //list.Add(this);
        }

        public virtual void Draw(SpriteBatch spriteBatch)
        {
            if (timerBar != null) timerBar.Draw(spriteBatch);
        }

        public virtual void Start(TimeSpan duration)
        {
            startTime = DateTime.Now;
            if (duration!=TimeSpan.Zero)
            {
                endTime = startTime + duration;
                this.duration = duration;

                timerBar = new GUIProgressBar(new Rectangle(Game1.GraphicsWidth - 120, 20, 100, 25), Color.Gold, 0.0f, null);  
            }

            endMessage = "The round has ended!";

            isRunning = true;
        }

        public virtual void Update(float deltaTime)
        {
            if (!isRunning) return;

            if (duration != TimeSpan.Zero)
            {
                double elapsedTime = (DateTime.Now - startTime).TotalSeconds;
                timerBar.BarSize = (float)(elapsedTime / duration.TotalSeconds);
            }
            //if (DateTime.Now >= endTime)
            //{
            //    End(endMessage);                
            //}
        }

        public virtual void End(string endMessage = "")
        {
            isRunning = false;

            if (endMessage != "" || this.endMessage == null) this.endMessage = endMessage;

            Game1.GameSession.EndShift(endMessage);
        }
        
        public static void Init()
        {
            new GameModePreset("Single Player", typeof(SinglePlayerMode), true);
            new GameModePreset("SandBox", typeof(GameMode), false);
            new GameModePreset("Traitor", typeof(TraitorMode), false);


            //new SinglePlayerMode("Single Player", true);
            //new GameMode("Sandbox");
            //new TraitorMode("Traitor");
        }
    }
}
