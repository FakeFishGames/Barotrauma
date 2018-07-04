using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;

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

        public static string GetDefaultFileName(string speciesName, AnimationType animType) => $"{speciesName.CapitaliseFirstInvariant()}{animType.ToString()}.xml";
        public static string GetDefaultFolder(string speciesName) => $"Content/Characters/{speciesName.CapitaliseFirstInvariant()}/Animations/";

        /// <summary>
        /// The file name can be partial. If left null of fails, will select the default.
        /// </summary>
        public static T GetAnimParams<T>(string speciesName, AnimationType animType, string fileName = null) where T : AnimationParams, new()
        {
            if (!animations.TryGetValue(speciesName, out Dictionary<AnimationType, AnimationParams> anims))
            {
                anims = new Dictionary<AnimationType, AnimationParams>();
                animations.Add(speciesName, anims);
            }
            string defaultFileName = GetDefaultFileName(speciesName, animType);
            fileName = fileName ?? defaultFileName;
            if (!anims.TryGetValue(animType, out AnimationParams anim))
            {
                string folder = GetDefaultFolder(speciesName);
                string defaultFile = folder + defaultFileName;
                var animFiles = Directory.GetFiles(folder);
                if (animFiles.None())
                {
                    throw new Exception("[AnimationParams] Could not find any animation files for the character from the path: " + folder);
                }
                string selectedFile = defaultFile;
                if (fileName != defaultFileName)
                {
                    selectedFile = animFiles.FirstOrDefault(p =>
                    {
                        string _p = p.ToLowerInvariant();
                        return _p.Contains(fileName.ToLowerInvariant()) && _p.Contains(animType.ToString().ToLowerInvariant());
                    });
                    if (selectedFile == null)
                    {
                        DebugConsole.NewMessage($"[AnimationParams] Could not find an animation file that matches the name {fileName} and the animation type {animType}. Using the default animations.", Color.Red);
                        selectedFile = defaultFile;
                    }
                }
                if (selectedFile == null)
                {
                    throw new Exception("[AnimationParams] Selected file null!");
                }
                DebugConsole.NewMessage($"[AnimationParams] Loading the animation params from {selectedFile}.", Color.Yellow);
                T a = new T();
                if (a.Load(selectedFile, animType))
                {
                    anims.Add(animType, a);
                }
                else
                {
                    DebugConsole.ThrowError($"[AnimationParams] Failed to load an animation {a} at {selectedFile} of type {animType} for the character {speciesName}");
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
