using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum AnimationType
    {
        NotDefined,
        Walk,
        Run,
        SwimSlow,
        SwimFast
    }

    abstract class GroundedMovementParams : AnimationParams
    {
        [Serialize("1.0, 1.0", true), Editable(DecimalCount = 2, ToolTip = "How big steps the character takes.")]
        public Vector2 StepSize
        {
            get;
            set;
        }

        [Serialize(float.NaN, true), Editable(DecimalCount = 2, ToolTip = "How high above the ground the character's head is positioned.")]
        public float HeadPosition { get; set; }

        [Serialize(float.NaN, true), Editable(DecimalCount = 2, ToolTip = "How high above the ground the character's torso is positioned.")]
        public float TorsoPosition { get; set; }

        [Serialize(0.75f, true), Editable(DecimalCount = 2, ToolTip = "The character's movement speed is multiplied with this value when moving backwards.")]
        public float BackwardsMovementMultiplier { get; set; }
    }

    abstract class SwimParams : AnimationParams
    {
        [Serialize(25.0f, true)]
        public float SteerTorque { get; set; }
    }

    abstract class AnimationParams : EditableParams
    {
        public string SpeciesName { get; private set; }
        public bool IsGroundedAnimation => AnimationType == AnimationType.Walk || AnimationType == AnimationType.Run;
        public bool IsSwimAnimation => AnimationType == AnimationType.SwimSlow || AnimationType == AnimationType.SwimFast;

        protected static Dictionary<string, Dictionary<string, AnimationParams>> allAnimations = new Dictionary<string, Dictionary<string, AnimationParams>>();

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

        [Serialize(1.0f, true), Editable(DecimalCount = 2)]
        public float Speed { get; set; }

        [Serialize(AnimationType.NotDefined, true), Editable]
        public virtual AnimationType AnimationType { get; protected set; }

        public static string GetDefaultFileName(string speciesName, AnimationType animType) => $"{speciesName.CapitaliseFirstInvariant()}{animType.ToString()}";
        public static string GetDefaultFolder(string speciesName) => $"Content/Characters/{speciesName.CapitaliseFirstInvariant()}/Animations/";
        public static string GetDefaultFile(string speciesName, AnimationType animType) => $"{GetDefaultFolder(speciesName)}{GetDefaultFileName(speciesName, animType)}.xml";

        protected static string GetFolder(string speciesName)
        {
            var folder = XMLExtensions.TryLoadXml(Character.GetConfigFile(speciesName)).Root?.Element("animations")?.GetAttributeString("folder", string.Empty);
            if (string.IsNullOrEmpty(folder) || folder.ToLowerInvariant() == "default")
            {
                folder = GetDefaultFolder(speciesName);
            }
            return folder;
        }

        /// <summary>
        /// Selects a random filepath from multiple paths, matching the specified animation type.
        /// </summary>
        public static string GetRandomFilePath(IEnumerable<string> filePaths, AnimationType type)
        {
            return filePaths.GetRandom(f => AnimationPredicate(f, type));
        }

        /// <summary>
        /// Selects all file paths that match the specified animation type.
        /// </summary>
        public static IEnumerable<string> FilterFilesByType(IEnumerable<string> filePaths, AnimationType type)
        {
            return filePaths.Where(f => AnimationPredicate(f, type));
        }

        private static bool AnimationPredicate(string filePath, AnimationType type)
        {
            var doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null) { return false; }
            return Enum.TryParse(doc.Root.GetAttributeString("AnimationType", "NotDefined"), out AnimationType fileType) && fileType == type;
        }

        public static T GetDefaultAnimParams<T>(string speciesName, AnimationType animType) where T : AnimationParams, new() => GetAnimParams<T>(speciesName, animType, GetDefaultFileName(speciesName, animType));

        /// <summary>
        /// If the file name is left null, a random file is selected. If fails, will select the default file. Note: Use the filename without the extensions, don't use the full path!
        /// If a custom folder is used, it's defined in the character info file.
        /// </summary>
        public static T GetAnimParams<T>(string speciesName, AnimationType animType, string fileName = null) where T : AnimationParams, new()
        {
            if (!allAnimations.TryGetValue(speciesName, out Dictionary<string, AnimationParams> anims))
            {
                anims = new Dictionary<string, AnimationParams>();
                allAnimations.Add(speciesName, anims);
            }
            if (fileName == null || !anims.TryGetValue(fileName, out AnimationParams anim))
            {
                string selectedFile = null;
                string folder = GetFolder(speciesName);
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder);
                    if (files.None())
                    {
                        DebugConsole.ThrowError($"[AnimationParams] Could not find any animation files from the folder: {folder}. Using the default animation.");
                        selectedFile = GetDefaultFile(speciesName, animType);
                    }
                    var filteredFiles = FilterFilesByType(files, animType);
                    if (filteredFiles.None())
                    {
                        DebugConsole.ThrowError($"[AnimationParams] Could not find any animation files that match the animation type {animType} from the folder: {folder}. Using the default animation.");
                        selectedFile = GetDefaultFile(speciesName, animType);
                    }
                    else if (string.IsNullOrEmpty(fileName))
                    {
                        // Files found, but none specified.
                        DebugConsole.Log($"[AnimationParams] Selecting random animation of type {animType} for {speciesName}");
                        selectedFile = filteredFiles.GetRandom();
                    }
                    else
                    {
                        selectedFile = filteredFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == fileName.ToLowerInvariant());
                        if (selectedFile == null)
                        {
                            DebugConsole.ThrowError($"[AnimationParams] Could not find an animation file that matches the name {fileName} and the animation type {animType}. Using the default animations.");
                            selectedFile = GetDefaultFile(speciesName, animType);
                        }
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"[Animationparams] Invalid directory: {folder}. Using the default animation.");
                    selectedFile = GetDefaultFile(speciesName, animType);
                }
                if (selectedFile == null)
                {
                    throw new Exception("[AnimationParams] Selected file null!");
                }
                DebugConsole.Log($"[AnimationParams] Loading animations from {selectedFile}.");
                T a = new T();
                if (a.Load(selectedFile, speciesName))
                {
                    if (!anims.ContainsKey(a.Name))
                    {
                        anims.Add(a.Name, a);
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"[AnimationParams] Failed to load an animation {a} at {selectedFile} of type {animType} for the character {speciesName}");
                }
                return a;
            }
            return (T)anim;
        }

        protected bool Load(string file, string speciesName)
        {
            if (Load(file))
            {
                SpeciesName = speciesName;
                return true;
            }
            return false;
        }

        protected override void UpdatePath(string newPath)
        {
            if (SpeciesName == null)
            {
                base.UpdatePath(newPath);
            }
            else
            {
                // Update the key by removing and re-adding the animation.
                if (allAnimations.TryGetValue(SpeciesName, out Dictionary<string, AnimationParams> animations))
                {
                    animations.Remove(Name);
                }
                base.UpdatePath(newPath);
                if (animations != null)
                {
                    if (!animations.ContainsKey(Name))
                    {
                        animations.Add(Name, this);
                    }
                }
            }
        }
    }
}
