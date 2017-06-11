using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class Skill
    {
        SkillPrefab prefab;

        string name;
        int level;

        static string[] levelNames = new string[] { 
            "Untrained", "Incompetent", "Novice", 
            "Adequate", "Competent", "Proficient", 
            "Professional", "Master", "Legendary" };

        public string Name
        {
            get { return name; }
        }

        public int Level
        {
            get { return level; }
            set { level = MathHelper.Clamp(value, 0, 100); }
        }

        public Skill(SkillPrefab prefab)
        {
            this.prefab = prefab;
            this.name = prefab.Name;

            this.level = (int)Rand.Range(prefab.LevelRange.X, prefab.LevelRange.Y);
        }

        public Skill(string name, int level)
        {
            this.name = name;

            this.level = level;
        }

        /// <summary>
        /// returns the "name" of some skill level (0-10 -> untrained, etc)
        /// </summary>
        public static string GetLevelName(int level)
        {
            level = MathHelper.Clamp(level, 0, 100);
            int scaledLevel = (int)Math.Floor((level / 100.0f) * levelNames.Length);

            return levelNames[Math.Min(scaledLevel, levelNames.Length - 1)];
        }
    }
}
