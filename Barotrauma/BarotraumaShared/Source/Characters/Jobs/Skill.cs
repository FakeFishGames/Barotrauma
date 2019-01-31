using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class Skill
    {
        private SkillPrefab prefab;
        
        private float level;

        static string[] levelNames = new string[] { 
            "Untrained", "Incompetent", "Novice", 
            "Adequate", "Competent", "Proficient", 
            "Professional", "Master", "Legendary" };

        string identifier;
        public string Identifier
        {
            get { return identifier; }
        }
        
        public float Level
        {
            get { return level; }
            set { level = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public Skill(SkillPrefab prefab)
        {
            this.prefab = prefab;
            this.identifier = prefab.Identifier;

            this.level = Rand.Range(prefab.LevelRange.X, prefab.LevelRange.Y, Rand.RandSync.Server);
        }

        public Skill(string identifier, float level)
        {
            this.identifier = identifier;
            this.level = level;
        }

        /// <summary>
        /// returns the "name" of some skill level (0-10 -> untrained, etc)
        /// </summary>
        public static string GetLevelName(float level)
        {
            level = MathHelper.Clamp(level, 0.0f, 100.0f);
            int scaledLevel = (int)Math.Floor((level / 100.0f) * levelNames.Length);

            return levelNames[Math.Min(scaledLevel, levelNames.Length - 1)];
        }
    }
}
