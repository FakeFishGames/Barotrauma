﻿using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using Barotrauma.IO;
using System;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum AnimationType
    {
        NotDefined = 0,
        Walk = 1,
        Run = 2,
        SwimSlow = 3,
        SwimFast = 4,
        Crouch = 5
    }

    abstract class GroundedMovementParams : AnimationParams
    {
        [Serialize("1.0, 1.0", IsPropertySaveable.Yes, description: "How big steps the character takes."), Editable(DecimalCount = 2, ValueStep = 0.01f)]
        public Vector2 StepSize
        {
            get;
            set;
        }

        [Serialize(0f, IsPropertySaveable.Yes, description: "How high above the ground the character's head is positioned."), Editable(DecimalCount = 2, ValueStep = 0.1f)]
        public float HeadPosition { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "How high above the ground the character's torso is positioned."), Editable(DecimalCount = 2, ValueStep = 0.1f)]
        public float TorsoPosition { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, description: "Separate multiplier for the head lift"), Editable(MinValueFloat = 0, MaxValueFloat = 2, ValueStep = 0.1f)]
        public float StepLiftHeadMultiplier { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "How much the body raises when taking a step."), Editable(MinValueFloat = 0, MaxValueFloat = 100, ValueStep = 0.1f)]
        public float StepLiftAmount { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool MultiplyByDir { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, description: "When does the body raise when taking a step. The default (0.5) is in the middle of the step."), Editable(MinValueFloat = -1, MaxValueFloat = 1, DecimalCount = 2, ValueStep = 0.1f)]
        public float StepLiftOffset { get; set; }

        [Serialize(2f, IsPropertySaveable.Yes, description: "How frequently the body raises when taking a step. The default is 2 (after every step)."), Editable(MinValueFloat = 0, MaxValueFloat = 10, ValueStep = 0.1f)]
        public float StepLiftFrequency { get; set; }

        [Serialize(0.75f, IsPropertySaveable.Yes, description: "The character's movement speed is multiplied with this value when moving backwards."), Editable(MinValueFloat = 0.1f, MaxValueFloat = 0.99f, DecimalCount = 2)]
        public float BackwardsMovementMultiplier { get; set; }
    }

    abstract class SwimParams : AnimationParams
    {
        [Serialize(25.0f, IsPropertySaveable.Yes, description: "Turning speed (or rather a force applied on the main collider to make it turn). Note that you can set a limb-specific steering forces too (additional)."), Editable(MinValueFloat = 0, MaxValueFloat = 1000, ValueStep = 1)]
        public float SteerTorque { get; set; }

        [Serialize(25.0f, IsPropertySaveable.Yes, description: "How much torque is used to move the legs."), Editable(MinValueFloat = 0, MaxValueFloat = 1000, ValueStep = 1)]
        public float LegTorque { get; set; }
    }

    abstract class AnimationParams : EditableParams, IMemorizable<AnimationParams>
    {
        public Identifier SpeciesName { get; private set; }
        public bool IsGroundedAnimation => AnimationType == AnimationType.Walk || AnimationType == AnimationType.Run || AnimationType == AnimationType.Crouch;
        public bool IsSwimAnimation => AnimationType == AnimationType.SwimSlow || AnimationType == AnimationType.SwimFast;

        protected static Dictionary<Identifier, Dictionary<string, AnimationParams>> allAnimations = new Dictionary<Identifier, Dictionary<string, AnimationParams>>();
        ///    allAnimations[speciesName][fileName]

        private float _movementSpeed;
        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(DecimalCount = 2, MinValueFloat = 0, MaxValueFloat = Ragdoll.MAX_SPEED, ValueStep = 0.1f)]
        public float MovementSpeed
        {
            get => _movementSpeed;
            set => _movementSpeed = value;
        }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The speed of the \"animation cycle\", i.e. how fast the character takes steps or moves the tail/legs/arms (the outcome depends what the clip is about)"),
            Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2, ValueStep = 0.01f)]
        public float CycleSpeed { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, IsPropertySaveable.Yes), Editable(-360f, 360f)]
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
        [Serialize(float.NaN, IsPropertySaveable.Yes), Editable(-360f, 360f)]
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

        [Serialize(50.0f, IsPropertySaveable.Yes, description: "How much torque is used to rotate the head to the correct orientation."), Editable(MinValueFloat = 0, MaxValueFloat = 1000, ValueStep = 1)]
        public float HeadTorque { get; set; }

        [Serialize(50.0f, IsPropertySaveable.Yes, description: "How much torque is used to rotate the torso to the correct orientation."), Editable(MinValueFloat = 0, MaxValueFloat = 1000, ValueStep = 1)]
        public float TorsoTorque { get; set; }

        [Serialize(25.0f, IsPropertySaveable.Yes, description: "How much torque is used to rotate the feet to the correct orientation."), Editable(MinValueFloat = 0, MaxValueFloat = 1000, ValueStep = 1)]
        public float FootTorque { get; set; }

        [Serialize(AnimationType.NotDefined, IsPropertySaveable.Yes), Editable]
        public virtual AnimationType AnimationType { get; protected set; }

        [Serialize(1f, IsPropertySaveable.Yes, description: "How much force is used to rotate the arms to the IK position."), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float ArmIKStrength { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, description: "How much force is used to rotate the hands to the IK position."), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float HandIKStrength { get; set; }

        public static string GetDefaultFileName(Identifier speciesName, AnimationType animType) => $"{speciesName.Value.CapitaliseFirstInvariant()}{animType}";
        public static string GetDefaultFile(Identifier speciesName, AnimationType animType) => Barotrauma.IO.Path.Combine(GetFolder(speciesName), $"{GetDefaultFileName(speciesName, animType)}.xml");

        public static string GetFolder(Identifier speciesName)
        {
            CharacterPrefab prefab = CharacterPrefab.FindBySpeciesName(speciesName);
            if (prefab?.ConfigElement == null)
            {
                DebugConsole.ThrowError($"Failed to find config file for '{speciesName}'");
                return string.Empty;
            }
            return GetFolder(prefab.ConfigElement, prefab.FilePath.Value);
        }

        private static string GetFolder(ContentXElement root, string filePath)
        {
            var folder = root?.GetChildElement("animations")?.GetAttributeContentPath("folder")?.Value;
            if (string.IsNullOrEmpty(folder) || folder.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                folder = IO.Path.Combine(IO.Path.GetDirectoryName(filePath), "Animations");
            }
            return folder.CleanUpPathCrossPlatform(true);
        }

        /// <summary>
        /// Selects a random filepath from multiple paths, matching the specified animation type.
        /// </summary>
        public static string GetRandomFilePath(IReadOnlyList<string> filePaths, AnimationType type)
        {
            return filePaths.GetRandom(f => AnimationPredicate(f, type), Rand.RandSync.ServerAndClient);
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

        public static T GetDefaultAnimParams<T>(Character character, AnimationType animType) where T : AnimationParams, new()
        {
            Identifier speciesName = character.SpeciesName;
            if (!character.VariantOf.IsEmpty
                && (character.Params.VariantFile?.Root?.GetChildElement("animations")?.GetAttributeStringUnrestricted("folder", null)).IsNullOrEmpty())
            {
                // Use the base animations defined in the base definition file.
                speciesName = character.VariantOf;
            }
            return GetAnimParams<T>(speciesName, animType, GetDefaultFileName(speciesName, animType));
        }

        /// <summary>
        /// If the file name is left null, default file is selected. If fails, will select the default file. Note: Use the filename without the extensions, don't use the full path!
        /// If a custom folder is used, it's defined in the character info file.
        /// </summary>
        public static T GetAnimParams<T>(Identifier speciesName, AnimationType animType, string fileName = null) where T : AnimationParams, new()
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
                        selectedFile = filteredFiles.FirstOrDefault(f => IO.Path.GetFileNameWithoutExtension(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
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
                var characterPrefab = CharacterPrefab.Prefabs[speciesName];
                T a = new T();
                if (a.Load(ContentPath.FromRaw(characterPrefab.ContentPackage, selectedFile), speciesName))
                {
                    fileName = IO.Path.GetFileNameWithoutExtension(selectedFile);
                    if (!anims.ContainsKey(fileName))
                    {
                        anims.Add(fileName, a);
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"[AnimationParams] Failed to load an animation {a} at {selectedFile} of type {animType} for the character {speciesName}",
                        contentPackage: characterPrefab.ContentPackage);
                }
                return a;
            }
            return (T)anim;
        }

        public static void ClearCache() => allAnimations.Clear();

        public static AnimationParams Create(string fullPath, Identifier speciesName, AnimationType animationType, Type type)
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
            if (type == typeof(HumanCrouchParams))
            {
                return Create<HumanCrouchParams>(fullPath, speciesName, animationType);
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
        public static T Create<T>(string fullPath, Identifier speciesName, AnimationType animationType) where T : AnimationParams, new()
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
            var fileName = IO.Path.GetFileNameWithoutExtension(fullPath);
            if (anims.ContainsKey(fileName))
            {
                DebugConsole.NewMessage($"[AnimationParams] Removing the old animation of type {animationType}.", Color.Red);
                anims.Remove(fileName);
            }
            var instance = new T();
            XElement animationElement = new XElement(GetDefaultFileName(speciesName, animationType), new XAttribute("animationtype", animationType.ToString()));
            instance.doc = new XDocument(animationElement);
            var characterPrefab = CharacterPrefab.Prefabs[speciesName];
            var contentPath = ContentPath.FromRaw(characterPrefab.ContentPackage, fullPath);
            instance.UpdatePath(contentPath);
            instance.IsLoaded = instance.Deserialize(animationElement);
            instance.Save();
            instance.Load(contentPath, speciesName);
            anims.Add(fileName, instance);
            DebugConsole.NewMessage($"[AnimationParams] New animation file of type {animationType} created.", Color.GhostWhite);
            return instance;
        }

        public bool Serialize() => base.Serialize();
        public bool Deserialize() => base.Deserialize();

        protected bool Load(ContentPath file, Identifier speciesName)
        {
            if (Load(file))
            {
                SpeciesName = speciesName;
                return true;
            }
            return false;
        }

        protected override void UpdatePath(ContentPath newPath)
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
                    case AnimationType.Crouch:
                        return typeof(HumanCrouchParams);
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
        public Memento<AnimationParams> Memento { get; protected set; } = new Memento<AnimationParams>();
        public abstract void StoreSnapshot();
        protected void StoreSnapshot<T>() where T : AnimationParams, new()
        {
            if (doc == null)
            {
                DebugConsole.ThrowError("[AnimationParams] The source XML Document is null!");
                return;
            }
            Serialize();
            var copy = new T
            {
                IsLoaded = true,
                doc = new XDocument(doc),
                Path = Path
            };
            copy.Deserialize();
            copy.Serialize();
            Memento.Store(copy);
        }
        public void Undo() => Deserialize(Memento.Undo().MainElement);
        public void Redo() => Deserialize(Memento.Redo().MainElement);
        public void ClearHistory() => Memento.Clear();
        #endregion
    }
}
