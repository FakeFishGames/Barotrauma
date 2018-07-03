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
        [Serialize(1.0f, true), Editable(0.1f, 10f)]
        public float Scale { get; set; }

        private static Dictionary<string, Dictionary<string, RagdollParams>> allRagdolls = new Dictionary<string, Dictionary<string, RagdollParams>>();

        public List<JointParams> Joints { get; private set; } = new List<JointParams>();

        /// <summary>
        /// The file name can be partial. If left null, will select randomly. If fails, will select the default file.
        /// </summary>
        public static T GetRagdollParams<T>(Character character, string fileName = null) where T : RagdollParams, new()
        {
            string speciesName = character.SpeciesName;
            if (!allRagdolls.TryGetValue(speciesName, out Dictionary<string, RagdollParams> ragdolls))
            {
                ragdolls = new Dictionary<string, RagdollParams>();
                allRagdolls.Add(speciesName, ragdolls);
            }
            string defaultPath = $"Content/Characters/{speciesName}/Ragdolls/";
            string defaultFileName = $"Default{speciesName}Ragdoll.xml";
            fileName = fileName ?? defaultFileName;
            if (!ragdolls.TryGetValue(fileName, out RagdollParams ragdoll))
            {
                XDocument characterConfigFile = XMLExtensions.TryLoadXml(character.ConfigPath);
                string firstLetter = speciesName.First().ToString().ToUpperInvariant();
                speciesName = firstLetter + speciesName.ToLowerInvariant().Substring(1);
                string folderPath = characterConfigFile.Root.Element("ragdoll").GetAttributeString("path", defaultPath);
                var ragdollPaths = Directory.GetFiles(folderPath);
                if (ragdollPaths.None())
                {
                    throw new Exception("[RagdollParams] Could not find any ragdoll param files for the character from the path: " + folderPath);
                }
                string selectedPath = null;
                if (fileName != defaultFileName)
                {
                    selectedPath = ragdollPaths.FirstOrDefault(p => p.Contains(fileName));
                    if (selectedPath == null)
                    {
                        DebugConsole.NewMessage($"[RagdollParams] Could not find a ragdoll param file that matches the name {fileName}. Using the default ragdoll.", Color.Red);
                        selectedPath = $"{folderPath}/{defaultFileName}";
                    }
                }
                if (selectedPath == null)
                {
                    selectedPath = ragdollPaths.GetRandom();
                }
                T r = new T();
                if (r.Load(selectedPath))
                {
                    ragdolls.Add(fileName, r);
                }
                else
                {
                    DebugConsole.ThrowError($"[RagdollParams] Failed to load ragdoll params {r} for character {speciesName} at {selectedPath}");
                }
                ragdoll = r;
            }
            return (T)ragdoll;
        }

        protected override bool Deserialize(XDocument doc)
        {
            DebugConsole.NewMessage($"setting up the base element {doc.Root.Name}", Color.Green);
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc.Root);
            Joints.Clear();
            foreach (var jointElement in doc.Root.Elements("joint"))
            {
                var joint = new JointParams();
                joint.Name = $"Joint {jointElement.Attribute("limb1").Value} - {jointElement.Attribute("limb2").Value}";
                joint.SerializableProperties = SerializableProperty.DeserializeProperties(joint, jointElement);
                Joints.Add(joint);
                DebugConsole.NewMessage($"Joint element {joint.Name} ready.", Color.Pink);
            }

            // TODO: deserialize all ragdoll sub elements here

            return SerializableProperties != null;
        }

        public override void AddToEditor(ParamsEditor editor)
        {
            base.AddToEditor(editor);
            Joints.ForEach(j => new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, j, false, true));
        }
    }

    class JointParams : ISerializableEntity
    {
        public string Name { get; set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

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
