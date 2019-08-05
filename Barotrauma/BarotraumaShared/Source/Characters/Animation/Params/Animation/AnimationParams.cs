using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;
using System.Linq;
using System.Xml.Linq;
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

        [Serialize(0f, true), Editable(DecimalCount = 2, ToolTip = "How high above the ground the character's head is positioned.")]
        public float HeadPosition { get; set; }

        [Serialize(0f, true), Editable(DecimalCount = 2, ToolTip = "How high above the ground the character's torso is positioned.")]
        public float TorsoPosition { get; set; }

        [Serialize(0.75f, true), Editable(MinValueFloat = 0.1f, MaxValueFloat = 0.99f, DecimalCount = 2, ToolTip = "The character's movement speed is multiplied with this value when moving backwards.")]
        public float BackwardsMovementMultiplier { get; set; }
    }

    abstract class SwimParams : AnimationParams
    {
        [Serialize(25.0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 500)]
        public float SteerTorque { get; set; }
    }

    abstract class AnimationParams : EditableParams
    {
        public string SpeciesName { get; private set; }
        public bool IsGroundedAnimation => AnimationType == AnimationType.Walk || AnimationType == AnimationType.Run;
        public bool IsSwimAnimation => AnimationType == AnimationType.SwimSlow || AnimationType == AnimationType.SwimFast;

        protected static Dictionary<string, Dictionary<string, AnimationParams>> allAnimations = new Dictionary<string, Dictionary<string, AnimationParams>>();

        [Serialize(1.0f, true), Editable(DecimalCount = 2)]
        public float MovementSpeed { get; set; }

        [Serialize(1.0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2, 
            ToolTip = "The speed of the \"animation cycle\", i.e. how fast the character takes steps or moves the tail/legs/arms (the outcome depends what the clip is about)")]
        public float CycleSpeed { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable(-360f, 360f)]
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
        [Serialize(float.NaN, true), Editable(-360f, 360f)]
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

        [Serialize(AnimationType.NotDefined, true), Editable]
        public virtual AnimationType AnimationType { get; protected set; }

        public static string GetDefaultFileName(string speciesName, AnimationType animType) => $"{speciesName.CapitaliseFirstInvariant()}{animType.ToString()}";
        public static string GetDefaultFile(string speciesName, AnimationType animType, ContentPackage contentPackage = null) 
            => Path.Combine(GetFolder(speciesName, contentPackage), $"{GetDefaultFileName(speciesName, animType)}.xml");

        public static string GetFolder(string speciesName, ContentPackage contentPackage = null)
        {
            string configFilePath = Character.GetConfigFile(speciesName, contentPackage);
            var folder = XMLExtensions.TryLoadXml(configFilePath)?.Root?.Element("animations")?.GetAttributeString("folder", string.Empty);
            if (string.IsNullOrEmpty(folder) || folder.ToLowerInvariant() == "default")
            {
                folder = Path.Combine(Path.GetDirectoryName(configFilePath), "Animations");
            }
            return folder;
        }

        /// <summary>
        /// Selects a random filepath from multiple paths, matching the specified animation type.
        /// </summary>
        public static string GetRandomFilePath(IEnumerable<string> filePaths, AnimationType type)
        {
            return filePaths.GetRandom(f => AnimationPredicate(f, type), Rand.RandSync.Server);
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
            var typeString = doc.Root.GetAttributeString("animationtype", null);
            if (string.IsNullOrWhiteSpace(typeString))
            {
                typeString = doc.Root.GetAttributeString("AnimationType", "NotDefined");
            }
            return Enum.TryParse(typeString, out AnimationType fileType) && fileType == type;
        }

        public static T GetDefaultAnimParams<T>(string speciesName, AnimationType animType) where T : AnimationParams, new() => GetAnimParams<T>(speciesName, animType, GetDefaultFileName(speciesName, animType));

        /// <summary>
        /// If the file name is left null, default file is selected. If fails, will select the default file. Note: Use the filename without the extensions, don't use the full path!
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
                        selectedFile = GetDefaultFile(speciesName, animType);
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

        public static AnimationParams Create(string fullPath, string speciesName, AnimationType animationType, Type type)
        {
            if (type == typeof(HumanWalkParams))
            {
                return Create<HumanWalkParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(HumanRunParams))
            {
                return Create<HumanRunParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(HumanSwimSlowParams))
            {
                return Create<HumanSwimSlowParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(HumanSwimFastParams))
            {
                return Create<HumanSwimFastParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(FishWalkParams))
            {
                return Create<FishWalkParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(FishRunParams))
            {
                return Create<FishRunParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(FishSwimSlowParams))
            {
                return Create<FishSwimSlowParams>(fullPath, speciesName, animationType);
            }
            if (type == typeof(FishSwimFastParams))
            {
                return Create<FishSwimFastParams>(fullPath, speciesName, animationType);
            }
            throw new NotImplementedException(type.ToString());
        }

        /// <summary>
        /// Note: Overrides old animations, if found!
        /// </summary>
        public static T Create<T>(string fullPath, string speciesName, AnimationType animationType) where T : AnimationParams, new()
        {
            if (animationType == AnimationType.NotDefined)
            {
                throw new Exception("Cannot create an animation file of type " + animationType.ToString());
            }
            if (!allAnimations.TryGetValue(speciesName, out Dictionary<string, AnimationParams> anims))
            {
                anims = new Dictionary<string, AnimationParams>();
                allAnimations.Add(speciesName, anims);
            }
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            if (anims.ContainsKey(fileName))
            {
                DebugConsole.NewMessage($"[AnimationParams] Removing the old animation of type {animationType}.", Color.Red);
                anims.Remove(fileName);
            }
            var instance = new T();
            XElement animationElement = new XElement(GetDefaultFileName(speciesName, animationType), new XAttribute("animationtype", animationType.ToString()));
            instance.doc = new XDocument(animationElement);
            instance.UpdatePath(fullPath);
            instance.IsLoaded = instance.Deserialize(animationElement);
            instance.Save();
            instance.Load(fullPath, speciesName);
            anims.Add(instance.Name, instance);
            DebugConsole.NewMessage($"[AnimationParams] New animation file of type {animationType} created.", Color.GhostWhite);
            return instance as T;
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

        protected static string ParseFootAngles(Dictionary<int, float> footAngles)
        {
            //convert to the format "id1:angle,id2:angle,id3:angle"
            return string.Join(",", footAngles.Select(kv => kv.Key + ": " + kv.Value.ToString("G", CultureInfo.InvariantCulture)).ToArray());
        }

        protected static void SetFootAngles(Dictionary<int, float> footAngles, string value)
        {
            footAngles.Clear();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            string[] keyValuePairs = value.Split(',');
            foreach (string joinedKvp in keyValuePairs)
            {
                string[] keyValuePair = joinedKvp.Split(':');
                if (keyValuePair.Length != 2 ||
                    !int.TryParse(keyValuePair[0].Trim(), out int limbIndex) ||
                    !float.TryParse(keyValuePair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
                {
                    DebugConsole.ThrowError("Failed to parse foot angles (" + value + ")");
                    continue;
                }
                footAngles[limbIndex] = angle;
            }
        }

        public static Type GetParamTypeFromAnimType(AnimationType type, bool isHumanoid)
        {
            if (isHumanoid)
            {
                switch (type)
                {
                    case AnimationType.Walk:
                        return typeof(HumanWalkParams);
                    case AnimationType.Run:
                        return typeof(HumanRunParams);
                    case AnimationType.SwimSlow:
                        return typeof(HumanSwimSlowParams);
                    case AnimationType.SwimFast:
                        return typeof(HumanSwimFastParams);
                    default:
                        throw new NotImplementedException(type.ToString());
                }
            }
            else
            {
                switch (type)
                {
                    case AnimationType.Walk:
                        return typeof(FishWalkParams);
                    case AnimationType.Run:
                        return typeof(FishRunParams);
                    case AnimationType.SwimSlow:
                        return typeof(FishSwimSlowParams);
                    case AnimationType.SwimFast:
                        return typeof(FishSwimFastParams);
                    default:
                        throw new NotImplementedException(type.ToString());
                }
            }
        }

        #region Memento
        protected void CreateSnapshot<T>() where T : AnimationParams, new()
        {
            Serialize();
            if (doc == null)
            {
                DebugConsole.ThrowError("[AnimationParams] The source XML Document is null!");
                return;
            }
            var copy = new T
            {
                IsLoaded = true,
                doc = new XDocument(doc)
            };
            copy.Deserialize();
            copy.Serialize();
            memento.Store(copy);
        }
        public override void Undo() => Deserialize(memento.Undo().MainElement);
        public override void Redo() => Deserialize(memento.Redo().MainElement);
        #endregion
    }
}
