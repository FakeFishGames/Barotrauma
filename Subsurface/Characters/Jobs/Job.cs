using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class Job
    {

        private JobPrefab prefab;

        private Dictionary<string, float> skills;

        public string Name
        {
            get { return prefab.Name; }
        }

        public string Description
        {
            get { return prefab.Description; }
        }

        public Job(JobPrefab jobPrefab)
        {
            prefab = jobPrefab;

            skills = new Dictionary<string, float>();
            foreach (KeyValuePair<string, Vector2> skill in prefab.skills)
            {
                skills.Add(skill.Key, Rand.Range(skill.Value.X, skill.Value.Y, false));
            }
        }

        public static Job Random()
        {
            JobPrefab prefab = JobPrefab.List[Rand.Int(JobPrefab.List.Count-1, false)];

            return new Job(prefab);
        }

        public float GetSkill(string skillName)
        {
            float skillLevel = 0.0f;
            skills.TryGetValue(skillName.ToLower(), out skillLevel);

            return skillLevel;
        }
    }
}
