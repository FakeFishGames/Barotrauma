using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Subsurface
{
    class GameModePreset
    {
        public static List<GameModePreset> list = new List<GameModePreset>();

        public ConstructorInfo Constructor;
        public string Name;
        public bool IsSinglePlayer;

        public string Description;

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
            return (GameMode)Constructor.Invoke(lobject);
        }
    }
}
