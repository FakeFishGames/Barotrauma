using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class HumanRagdollParams : RagdollParams { }
    class FishRagdollParams : RagdollParams { }

    class RagdollParams : EditableParams
    {
        [Serialize(1.0f, true), Editable(0.5f, 2f)]
        public float LimbScale { get; set; }

        [Serialize(1.0f, true), Editable(0.5f, 2f)]
        public float JointScale { get; set; }

        private static Dictionary<string, Dictionary<string, RagdollParams>> allRagdolls = new Dictionary<string, Dictionary<string, RagdollParams>>();

        public List<JointParams> Joints { get; private set; } = new List<JointParams>();

        /// <summary>
        /// Returns the path to the default folder without the filename.
        /// </summary>
        public static string GetDefaultPath(string speciesName) => $"Content/Characters/{speciesName.CapitaliseFirstInvariant()}/Ragdolls/";
        public static string GetDefaultFileName(string speciesName) => $"default{speciesName.CapitaliseFirstInvariant()}ragdoll.xml";      

        /// <summary>
        /// Returns the path that is defined in the character config file. Does not include the file name.
        /// </summary>
        public static string GetPath(string speciesName) 
            => XMLExtensions.TryLoadXml(Character.GetConfigFile(speciesName)).Root.Element("ragdoll").GetAttributeString("folder", GetDefaultPath(speciesName));

        /// <summary>
        /// The file name can be partial. If left null, will select randomly. If fails, will select the default file.
        /// </summary>
        public static T GetRagdollParams<T>(string speciesName, string fileName = null) where T : RagdollParams, new()
        {
            if (!allRagdolls.TryGetValue(speciesName, out Dictionary<string, RagdollParams> ragdolls))
            {
                ragdolls = new Dictionary<string, RagdollParams>();
                allRagdolls.Add(speciesName, ragdolls);
            }
            string defaultFileName = GetDefaultFileName(speciesName);
            fileName = fileName ?? defaultFileName;
            if (!ragdolls.TryGetValue(fileName, out RagdollParams ragdoll))
            {
                string folder = GetPath(speciesName);
                string defaultFile = folder + defaultFileName;
                var ragdollFiles = Directory.GetFiles(folder);
                if (ragdollFiles.None())
                {
                    throw new Exception("[RagdollParams] Could not find any ragdoll param files for the character from the path: " + folder);
                }
                string selectedFile = null;
                if (fileName != defaultFileName)
                {
                    selectedFile = ragdollFiles.FirstOrDefault(p => p.ToLowerInvariant().Contains(fileName.ToLowerInvariant()));
                    if (selectedFile == null)
                    {
                        DebugConsole.NewMessage($"[RagdollParams] Could not find a ragdoll param file that matches the name {fileName}. Using the default ragdoll.", Color.Red);
                        selectedFile = defaultFile;
                    }
                }
                if (selectedFile == null)
                {
                    selectedFile = ragdollFiles.GetRandom();
                }
                if (selectedFile == null)
                {
                    selectedFile = defaultFile;
                }
                if (selectedFile == null)
                {
                    throw new Exception("[RagdollParams] Selected file null!");
                }
                DebugConsole.NewMessage($"[RagdollParams] Loading ragdoll params from {selectedFile}.", Color.Orange);
                T r = new T();
                if (r.Load(selectedFile))
                {
                    ragdolls.Add(fileName, r);
                }
                else
                {
                    DebugConsole.ThrowError($"[RagdollParams] Failed to load ragdoll params {r} at {selectedFile} for the character {speciesName}");
                }
                ragdoll = r;
            }
            return (T)ragdoll;
        }

        protected override bool Deserialize(XElement element)
        {
            base.Deserialize(element);
            Joints.Clear();
            foreach (var jointElement in element.Elements("joint"))
            {
                var joint = new JointParams();
                joint.Name = $"Joint {jointElement.Attribute("limb1").Value} - {jointElement.Attribute("limb2").Value}";
                joint.SerializableProperties = SerializableProperty.DeserializeProperties(joint, jointElement);
                joint.Element = jointElement;
                Joints.Add(joint);
                //DebugConsole.NewMessage($"Joint element {joint.Name} ready.", Color.Pink);
            }

            // TODO: deserialize all ragdoll sub elements here

            return SerializableProperties != null;
        }

        protected override bool Serialize(XElement element)
        {
            base.Serialize(element);
            Joints.ForEach(j => SerializableProperty.SerializeProperties(j, j.Element, true));
            return true;
        }

#if CLIENT
        public override void AddToEditor(ParamsEditor editor)
        {
            base.AddToEditor(editor);
            Joints.ForEach(j => new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, j, false, true));
        }
#endif
    }

    class JointParams : ISerializableEntity
    {
        public string Name { get; set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }
        public XElement Element { get; set; }

        [Serialize(true, true), Editable]
        public bool CanBeSevered { get; set; }

        [Serialize(-1, true), Editable(0, 255)]
        public int Limb1 { get; set; }

        [Serialize(-1, true), Editable(0, 255)]
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
}
