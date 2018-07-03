using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    public enum AnimationType
    {
        Walk,
        Run,
        SwimSlow,
        SwimFast
    }

    abstract class GroundedMovementParams : AnimationParams
    {
        [Serialize("1.0,1.0", true), Editable]
        public Vector2 StepSize
        {
            get;
            set;
        }

        [Serialize(float.NaN, true), Editable]
        public float HeadPosition { get; set; }

        [Serialize(float.NaN, true), Editable]
        public float TorsoPosition { get; set; }
    }

    abstract class SwimParams : AnimationParams
    {
        [Serialize(25.0f, true), Editable]
        public float SteerTorque { get; set; }
    }

    abstract class AnimationParams : EditableParams
    {
        public virtual AnimationType AnimationType { get; private set; }
        public bool IsGroundedAnimation => AnimationType == AnimationType.Walk || AnimationType == AnimationType.Run;
        public bool IsSwimAnimation => AnimationType == AnimationType.SwimSlow || AnimationType == AnimationType.SwimFast;

        protected static Dictionary<string, Dictionary<AnimationType, AnimationParams>> animations = new Dictionary<string, Dictionary<AnimationType, AnimationParams>>();

        [Serialize(1.0f, true), Editable]
        public float Speed
        {
            get;
            set;
        }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable]
        public float HeadAngle
        {
            get => float.IsNaN(HeadAngleInRadians) ? float.NaN : MathHelper.ToDegrees(HeadAngleInRadians);
            set
            {
                if (!float.IsNaN(value))
                {
                    HeadAngleInRadians = MathHelper.ToRadians(value);
                }
            }
        }
        public float HeadAngleInRadians { get; private set; } = float.NaN;

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable]
        public float TorsoAngle
        {
            get => float.IsNaN(TorsoAngleInRadians) ? float.NaN : MathHelper.ToDegrees(TorsoAngleInRadians);
            set
            {
                if (!float.IsNaN(value))
                {
                    TorsoAngleInRadians = MathHelper.ToRadians(value);
                }
            }
        }
        public float TorsoAngleInRadians { get; private set; } = float.NaN;

        public static T GetAnimParams<T>(Character character, AnimationType type) where T : AnimationParams, new()
        {
            string speciesName = character.SpeciesName;
            if (!animations.TryGetValue(speciesName, out Dictionary<AnimationType, AnimationParams> anims))
            {
                anims = new Dictionary<AnimationType, AnimationParams>();
                animations.Add(speciesName, anims);
            }
            if (!anims.TryGetValue(type, out AnimationParams anim))
            {
                XDocument characterConfigFile = XMLExtensions.TryLoadXml(character.ConfigPath);
                string firstLetter = speciesName.First().ToString().ToUpperInvariant();
                speciesName = firstLetter + speciesName.ToLowerInvariant().Substring(1);
                DebugConsole.NewMessage($"Loading animations of type {type} from {character.ConfigPath} using the species name {speciesName}.", Color.Orange);
                string animType = type.ToString();
                string defaultPath = $"Content/Characters/{speciesName}/Animations/{speciesName}{animType}.xml";
                string animPath = characterConfigFile.Root.Element("animation").GetAttributeString("path", defaultPath);
                animPath = animPath.Replace("[ANIMTYPE]", animType);
                T a = new T();
                if (a.Load(animPath, type))
                {
                    anims.Add(type, a);
                }
                else
                {
                    DebugConsole.ThrowError($"[AnimationParams] Failed to load an animation {a} of type {type} at {animPath}");
                }
                anim = a;
        }
            return (T)anim;
        }

        protected bool Load(string file, AnimationType type)
        {
            AnimationType = type;
            return Load(file);
        }
    }
}
