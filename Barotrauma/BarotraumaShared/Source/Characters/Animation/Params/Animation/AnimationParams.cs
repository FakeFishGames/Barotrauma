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
        [Serialize("1.0, 1.0", true), Editable]
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
        public bool IsGroundedAnimation => AnimationType == AnimationType.Walk || AnimationType == AnimationType.Run;
        public bool IsSwimAnimation => AnimationType == AnimationType.SwimSlow || AnimationType == AnimationType.SwimFast;

        protected static Dictionary<string, Dictionary<string, AnimationParams>> animations = new Dictionary<string, Dictionary<string, AnimationParams>>();

        [Serialize(AnimationType.NotDefined, true)]
        public virtual AnimationType AnimationType { get; protected set; }

        [Serialize(1.0f, true), Editable]
        public float Speed { get; set; }

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

        /// <summary>
        /// If the file name is left null, a random file is selected. If fails, will select the default file. Note: Use the filename without the extensions, don't use the full path!
        /// If a custom folder is used, it's defined in the character info file.
        /// </summary>
        public static T GetAnimParams<T>(string speciesName, AnimationType animType, string fileName = null) where T : AnimationParams, new()
        {
            if (!animations.TryGetValue(speciesName, out Dictionary<string, AnimationParams> anims))
            {
                anims = new Dictionary<string, AnimationParams>();
                animations.Add(speciesName, anims);
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
                        selectedFile = filteredFiles.GetRandom();
                    }
                    else
                    {
                        // Then check if a file matches the name ignoring the case
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
                DebugConsole.NewMessage($"[AnimationParams] Loading animations from {selectedFile}.", Color.Yellow);
                T a = new T();
                if (a.Load(selectedFile))
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
    }
}
