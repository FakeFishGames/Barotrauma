using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class AITarget
    {
        public static List<AITarget> List = new List<AITarget>();

        public Entity Entity
        {
            get;
            private set;
        }
        
        private float soundRange;
        private float sightRange;

        /// <summary>
        /// How long does it take for the ai target to fade out if not kept alive.
        /// </summary>
        public float FadeOutTime { get; private set; }
        
        public float SoundRange
        {
            get { return soundRange; }
            set { soundRange = MathHelper.Clamp(value, MinSoundRange, MaxSoundRange); }
        }

        public float SightRange
        {
            get { return sightRange; }
            set { sightRange = MathHelper.Clamp(value, MinSightRange, MaxSightRange); }
        }

        private float sectorRad = MathHelper.TwoPi;
        public float SectorDegrees
        {
            get { return MathHelper.ToDegrees(sectorRad); }
            set { sectorRad = MathHelper.ToRadians(value); }
        }

        private Vector2 sectorDir;
        public Vector2 SectorDir
        {
            get { return sectorDir; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Invalid AITarget sector direction (" + value + ")\n" + Environment.StackTrace;
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.SectorDir:" + Entity?.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }
                sectorDir = value;
            }
        }

        public float SonarDisruption
        {
            get;
            set;
        }

        public string SonarLabel;

        public bool Enabled = true;

        public float MinSoundRange, MinSightRange;
        public float MaxSoundRange = 100000, MaxSightRange = 100000;

        public TargetType Type { get; private set; }

        public enum TargetType
        {
            Any,
            HumanOnly,
            EnemyOnly
        }

        public Vector2 WorldPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace);
                    return Vector2.Zero;
                }

                return Entity.WorldPosition;
            }
        }

        public Vector2 SimPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace);
                    return Vector2.Zero;
                }

                return Entity.SimPosition;
            }
        }

        public AITarget(Entity e, XElement element) : this(e)
        {
            SightRange = element.GetAttributeFloat("sightrange", 0.0f);
            SoundRange = element.GetAttributeFloat("soundrange", 0.0f);
            MinSightRange = element.GetAttributeFloat("minsightrange", 0f);
            MinSoundRange = element.GetAttributeFloat("minsoundrange", 0f);
            MaxSightRange = element.GetAttributeFloat("maxsightrange", SightRange);
            MaxSoundRange = element.GetAttributeFloat("maxsoundrange", SoundRange);
            FadeOutTime = element.GetAttributeFloat("fadeouttime", FadeOutTime);
            SonarDisruption = element.GetAttributeFloat("sonardisruption", 0.0f);
            SonarLabel = element.GetAttributeString("sonarlabel", "");
            string typeString = element.GetAttributeString("type", "Any");
            if (Enum.TryParse(typeString, out TargetType t))
            {
                Type = t;
            }
        }

        public AITarget(Entity e)
        {
            Entity = e;
            List.Add(this);
        }

        public void Update(float deltaTime)
        {
            if (FadeOutTime > 0)
            {
                // The aitarget goes silent/invisible if the components don't keep it active
                SightRange -= deltaTime * (MaxSightRange / FadeOutTime);
                SoundRange -= deltaTime * (MaxSoundRange / FadeOutTime);
            }
            else
            {
                // Static ai targets
                SightRange = MaxSightRange;
                SoundRange = MaxSoundRange;
            }
        }

        public bool IsWithinSector(Vector2 worldPosition)
        {
            if (sectorRad >= MathHelper.TwoPi) return true;

            Vector2 diff = worldPosition - WorldPosition;
            return MathUtils.GetShortestAngle(MathUtils.VectorToAngle(diff), MathUtils.VectorToAngle(sectorDir)) <= sectorRad * 0.5f;
        }

        public void Remove()
        {
            List.Remove(this);
            Entity = null;
        }
    }
}
