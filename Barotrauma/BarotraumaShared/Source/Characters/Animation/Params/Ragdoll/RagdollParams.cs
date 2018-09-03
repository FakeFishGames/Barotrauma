using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class HumanRagdollParams : RagdollParams
    {
        public static HumanRagdollParams GetRagdollParams(string fileName = null) => GetRagdollParams<HumanRagdollParams>("human", fileName);
        public static HumanRagdollParams GetDefaultRagdollParams() => GetDefaultRagdollParams<HumanRagdollParams>("human");
    }

    class FishRagdollParams : RagdollParams
    {
        public static FishRagdollParams GetDefaultRagdollParams(string speciesName) => GetDefaultRagdollParams<FishRagdollParams>(speciesName);
    }

    class RagdollParams : EditableParams
    {
        public string SpeciesName { get; private set; }

        [Serialize(45f, true), Editable(0f, 1000f)]
        public float ColliderHeightFromFloor { get; set; }

        [Serialize(1.0f, true), Editable(0.5f, 2f)]
        public float LimbScale { get; set; }

        [Serialize(1.0f, true), Editable(0.5f, 2f)]
        public float JointScale { get; set; }

        private static Dictionary<string, Dictionary<string, RagdollParams>> allRagdolls = new Dictionary<string, Dictionary<string, RagdollParams>>();

        public List<LimbParams> Limbs { get; private set; } = new List<LimbParams>();
        public List<JointParams> Joints { get; private set; } = new List<JointParams>();
        protected IEnumerable<RagdollSubParams> GetAllSubParams() => Limbs.Select(j => j as RagdollSubParams).Concat(Joints.Select(j => j as RagdollSubParams));

        public XElement MainElement => Doc.Root;

        public static string GetDefaultFileName(string speciesName) => $"{speciesName.CapitaliseFirstInvariant()}DefaultRagdoll";
        public static string GetDefaultFolder(string speciesName) => $"Content/Characters/{speciesName.CapitaliseFirstInvariant()}/Ragdolls/";
        public static string GetDefaultFile(string speciesName) => $"{GetDefaultFolder(speciesName)}{GetDefaultFileName(speciesName)}.xml";

        protected static string GetFolder(string speciesName)
        {
            var folder = XMLExtensions.TryLoadXml(Character.GetConfigFile(speciesName)).Root?.Element("ragdolls")?.GetAttributeString("folder", string.Empty);
            if (string.IsNullOrEmpty(folder) || folder.ToLowerInvariant() == "default")
            {
                folder = GetDefaultFolder(speciesName);
            }
            return folder;
        }

        public static T GetDefaultRagdollParams<T>(string speciesName) where T : RagdollParams, new() => GetRagdollParams<T>(speciesName, GetDefaultFileName(speciesName));

        /// <summary>
        /// If the file name is left null, a random file is selected. If fails, will select the default file.  Note: Use the filename without the extensions, don't use the full path!
        /// If a custom folder is used, it's defined in the character info file.
        /// </summary>
        public static T GetRagdollParams<T>(string speciesName, string fileName = null) where T : RagdollParams, new()
        {
            if (!allRagdolls.TryGetValue(speciesName, out Dictionary<string, RagdollParams> ragdolls))
            {
                ragdolls = new Dictionary<string, RagdollParams>();
                allRagdolls.Add(speciesName, ragdolls);
            }
            if (fileName == null || !ragdolls.TryGetValue(fileName, out RagdollParams ragdoll))
            {
                string selectedFile = null;
                string folder = GetFolder(speciesName);
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder);
                    if (files.None())
                    {
                        DebugConsole.ThrowError($"[RagdollParams] Could not find any ragdoll files from the folder: {folder}. Using the default ragdoll.");
                        selectedFile = GetDefaultFile(speciesName);
                    }
                    else if (string.IsNullOrEmpty(fileName))
                    {
                        // Files found, but none specified
                        DebugConsole.NewMessage($"[RagdollParams] Selecting random ragdoll for {speciesName}", Color.White);
                        selectedFile = files.GetRandom();
                    }
                    else
                    {
                        selectedFile = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == fileName.ToLowerInvariant());
                        if (selectedFile == null)
                        {
                            DebugConsole.ThrowError($"[RagdollParams] Could not find a ragdoll file that matches the name {fileName}. Using the default ragdoll.");
                            selectedFile = GetDefaultFile(speciesName);
                        }
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"[RagdollParams] Invalid directory: {folder}. Using the default ragdoll.");
                    selectedFile = GetDefaultFile(speciesName);
                }
                if (selectedFile == null)
                {
                    throw new Exception("[RagdollParams] Selected file null!");
                }
                DebugConsole.NewMessage($"[RagdollParams] Loading ragdoll from {selectedFile}.", Color.Orange);
                T r = new T();
                if (r.Load(selectedFile, speciesName))
                {
                    if (!ragdolls.ContainsKey(r.Name))
                    {
                        ragdolls.Add(r.Name, r);
                    }
                }
                else
                {
                    DebugConsole.ThrowError($"[RagdollParams] Failed to load ragdoll {r} at {selectedFile} for the character {speciesName}");
                }
                return r;
            }
            return (T)ragdoll;
        }

        protected override void UpdatePath(string newPath)
        {
            if (SpeciesName == null)
            {
                base.UpdatePath(newPath);
            }
            else
            {
                // Update the key by removing and re-adding the ragdoll.
                if (allRagdolls.TryGetValue(SpeciesName, out Dictionary<string, RagdollParams> ragdolls))
                {
                    ragdolls.Remove(Name);
                }
                base.UpdatePath(newPath);
                if (ragdolls != null)
                {
                    if (!ragdolls.ContainsKey(Name))
                    {
                        ragdolls.Add(Name, this);
                    }
                }
            }
        }

        protected bool Load(string file, string speciesName)
        {
            if (Load(file))
            {
                SpeciesName = speciesName;
                CreateLimbs();
                CreateJoints();
                return true;
            }
            return false;
        }

        protected void CreateLimbs()
        {
            Limbs.Clear();
            foreach (var element in MainElement.Elements("limb"))
            {
                Limbs.Add(new LimbParams(element, this));
            }
        }

        protected void CreateJoints()
        {
            Joints.Clear();
            foreach (var element in MainElement.Elements("joint"))
            {
                Joints.Add(new JointParams(element, this));
            }
        }

        protected override bool Deserialize(XElement element)
        {
            if (base.Deserialize(element))
            {
                GetAllSubParams().ForEach(p => p.Deserialize());
                return true;
            }
            return false;
        }

        protected override bool Serialize(XElement element)
        {
            if (base.Serialize(element))
            {
                GetAllSubParams().ForEach(p => p.Serialize());
                return true;
            }
            return false;
        }

#if CLIENT
        public override void AddToEditor(ParamsEditor editor)
        {
            base.AddToEditor(editor);
            GetAllSubParams().ForEach(p => p.AddToEditor(editor));
        }
#endif
    }

    class JointParams : RagdollSubParams
    {
        public JointParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
        {
            Name = $"Joint {element.Attribute("limb1").Value} - {element.Attribute("limb2").Value}";
        }

        [Serialize(-1, true)]
        public int Limb1 { get; set; }

        [Serialize(-1, true)]
        public int Limb2 { get; set; }

        /// <summary>
        /// Should be converted to sim units.
        /// </summary>
        [Serialize("1.0, 1.0", true), Editable]
        public Vector2 Limb1Anchor { get; set; }

        /// <summary>
        /// Should be converted to sim units.
        /// </summary>
        [Serialize("1.0, 1.0", true), Editable]
        public Vector2 Limb2Anchor { get; set; }

        [Serialize(true, true), Editable]
        public bool CanBeSevered { get; set; }

        [Serialize(true, true), Editable]
        public bool LimitEnabled { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable(-360f, 360f)]
        public float UpperLimit { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(float.NaN, true), Editable(-360f, 360f)]
        public float LowerLimit { get; set; }
    }

    class LimbParams : RagdollSubParams
    {
        public LimbParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
        {
            Name = $"Limb {element.Attribute("id").Value}";
            var spriteElement = element.Element("sprite");
            if (spriteElement != null)
            {
                normalSpriteParams = new SpriteParams(spriteElement, ragdoll);
                SubParams.Add(normalSpriteParams);
            }
            var damagedElement = element.Element("damagedsprite");
            if (damagedElement != null)
            {
                damagedSpriteParams = new SpriteParams(damagedElement, ragdoll);
                SubParams.Add(damagedSpriteParams);
            }
            var deformElement = element.Element("deformablesprite");
            if (deformElement != null)
            {
                deformSpriteParams = new SpriteParams(deformElement, ragdoll);
                SubParams.Add(deformSpriteParams);
            }
        }

        public readonly SpriteParams normalSpriteParams;
        public readonly SpriteParams damagedSpriteParams;
        public readonly SpriteParams deformSpriteParams;

        // TODO: decide which properties should be editable in the editor and which only via xml

        /// <summary>
        /// TODO: editing this in-game doesn't currently have any effect
        /// </summary>
        [Serialize(-1, true)]
        public int ID { get; set; }

        /// <summary>
        /// TODO: editing this in-game doesn't currently have any effect
        /// </summary>
        [Serialize(LimbType.None, true), Editable]
        public LimbType Type { get; set; }

        [Serialize(false, true), Editable]
        public bool Flip { get; set; }

        [Serialize(0, true), Editable]
        public int HealthIndex { get; set; }

        [Serialize(0f, true), Editable]
        public float AttackPriority { get; set; }

        [Serialize(0f, true), Editable]
        public float SteerForce { get; set; }

        [Serialize("0, 0", true), Editable]
        public Vector2 StepOffset { get; set; }

        // Following params are not used right now. The params are assigned to a PhysicsBody.

        //[Serialize(0f, true)]
        //public float Radius { get; set; }

        //[Serialize(0f, true)]
        //public float Height { get; set; }

        //[Serialize(0f, true)]
        //public float Mass { get; set; }

        //[Serialize(false, true)]
        //public bool IgnoreCollisions { get; set; }
    }

    class SpriteParams : RagdollSubParams
    {
        public SpriteParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
        {
            Name = element.Name.ToString();
        }

        // TODO: decide which properties should be editable in the editor and which only via xml

        [Serialize("0, 0, 0, 0", true), Editable]
        public Rectangle SourceRect { get; set; }

        [Serialize("0.5, 0.5", true), Editable]
        public Vector2 Origin { get; set; }

        [Serialize(0f, true), Editable]
        public float Depth { get; set; }

        //[Serialize("", true)]
        //public string Texture { get; set; }
    }

    abstract class RagdollSubParams : ISerializableEntity
    {
        public string Name { get; protected set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
        public XElement Element { get; private set; }
        public List<RagdollSubParams> SubParams { get; set; } = new List<RagdollSubParams>();
        public RagdollParams Ragdoll { get; private set; }

        public RagdollSubParams(XElement element, RagdollParams ragdoll)
        {
            Element = element;
            Ragdoll = ragdoll;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public virtual bool Deserialize()
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, Element);
            SubParams.ForEach(sp => sp.Deserialize());
            return SerializableProperties != null;
        }

        public virtual bool Serialize()
        {
            SerializableProperty.SerializeProperties(this, Element, true);
            SubParams.ForEach(sp => sp.Serialize());
            return true;
        }

     #if CLIENT
        public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
        public virtual void AddToEditor(ParamsEditor editor)
        {
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, false, true);
            SubParams.ForEach(sp => sp.AddToEditor(editor));
        }
     #endif
    }
}
