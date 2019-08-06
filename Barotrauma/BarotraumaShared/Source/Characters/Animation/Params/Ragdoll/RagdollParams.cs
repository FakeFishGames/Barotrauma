using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using System.Xml;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class HumanRagdollParams : RagdollParams
    {
        public static HumanRagdollParams GetRagdollParams(string speciesName, string fileName = null) => GetRagdollParams<HumanRagdollParams>(speciesName, fileName);
        public static HumanRagdollParams GetDefaultRagdollParams(string speciesName) => GetDefaultRagdollParams<HumanRagdollParams>(speciesName);
    }

    class FishRagdollParams : RagdollParams
    {
        public static FishRagdollParams GetDefaultRagdollParams(string speciesName) => GetDefaultRagdollParams<FishRagdollParams>(speciesName);
    }

    class RagdollParams : EditableParams
    {
        public const float MIN_SCALE = 0.1f;
        public const float MAX_SCALE = 2;

        public string SpeciesName { get; private set; }

        [Serialize(0f, true), Editable(-360, 360, ToolTip = "Rotation offset (in degrees) used for animations and widgets. If the sprites in the sheet are in different orientations, use the orientation of the torso for the final version of your character (while editing the character in the editor, you can change the orientation freely).")]
        public float SpritesheetOrientation { get; set; }

        private float limbScale;
        [Serialize(1.0f, true), Editable(MIN_SCALE, MAX_SCALE, DecimalCount = 3)]
        public float LimbScale { get { return limbScale; } set { limbScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); } }

        private float jointScale;
        [Serialize(1.0f, true), Editable(MIN_SCALE, MAX_SCALE, DecimalCount = 3)]
        public float JointScale { get { return jointScale; } set { jointScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); } }

        // Don't show in the editor, because shouldn't be edited in runtime.  Requires that the limb scale and the collider sizes are adjusted. TODO: automatize.
        [Serialize(1f, false)]
        public float TextureScale { get; set; }

        [Serialize(45f, true), Editable(0f, 1000f)]
        public float ColliderHeightFromFloor { get; set; }

        [Serialize(50f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
        public float ImpactTolerance { get; set; }

        [Serialize(true, true), Editable]
        public bool CanEnterSubmarine { get; set; }

        [Serialize(true, true), Editable]
        public bool Draggable { get; set; }

        private static Dictionary<string, Dictionary<string, RagdollParams>> allRagdolls = new Dictionary<string, Dictionary<string, RagdollParams>>();

        public List<ColliderParams> ColliderParams { get; private set; } = new List<ColliderParams>();
        public List<LimbParams> Limbs { get; private set; } = new List<LimbParams>();
        public List<JointParams> Joints { get; private set; } = new List<JointParams>();

        protected IEnumerable<RagdollSubParams> GetAllSubParams() =>
            ColliderParams.Select(c => c as RagdollSubParams)
            .Concat(Limbs.Select(j => j as RagdollSubParams)
            .Concat(Joints.Select(j => j as RagdollSubParams)));

        public static string GetDefaultFileName(string speciesName) => $"{speciesName.CapitaliseFirstInvariant()}DefaultRagdoll";
        public static string GetDefaultFile(string speciesName, ContentPackage contentPackage = null)
            => Path.Combine(GetFolder(speciesName, contentPackage), $"{GetDefaultFileName(speciesName)}.xml");

        private static readonly object[] dummyParams = new object[]
        {
            new XAttribute("type", "Dummy"),
            new XElement("collider", new XAttribute("radius", 1)),
            new XElement("limb",
                new XAttribute("id", 0),
                new XAttribute("type", LimbType.Head.ToString()),
                new XAttribute("width", 1),
                new XAttribute("height", 1),
                new XElement("sprite",
                    new XAttribute("sourcerect", $"0, 0, 1, 1")))
        };

        public static string GetFolder(string speciesName, ContentPackage contentPackage = null)
        {
            string configFilePath = Character.GetConfigFile(speciesName, contentPackage);
            if (!Character.TryGetConfigFile(configFilePath, out XDocument configFile))
            {
                DebugConsole.ThrowError($"Failed to load config file: {configFilePath} for '{speciesName}'");
                return string.Empty;
            }
            var folder = configFile.Root?.Element("ragdolls")?.GetAttributeString("folder", string.Empty);
            if (string.IsNullOrEmpty(folder) || folder.ToLowerInvariant() == "default")
            {
                folder = Path.Combine(Path.GetDirectoryName(configFilePath), "Ragdolls") + Path.DirectorySeparatorChar;
            }
            return folder;
        }

        public static T GetDefaultRagdollParams<T>(string speciesName) where T : RagdollParams, new() => GetRagdollParams<T>(speciesName, GetDefaultFileName(speciesName));

        /// <summary>
        /// If the file name is left null, default file is selected. If fails, will select the default file.  Note: Use the filename without the extensions, don't use the full path!
        /// If a custom folder is used, it's defined in the character info file.
        /// </summary>
        public static T GetRagdollParams<T>(string speciesName, string fileName = null) where T : RagdollParams, new()
        {
            if (!allRagdolls.TryGetValue(speciesName, out Dictionary<string, RagdollParams> ragdolls))
            {
                ragdolls = new Dictionary<string, RagdollParams>();
                allRagdolls.Add(speciesName, ragdolls);
            }
            if (string.IsNullOrEmpty(fileName) || !ragdolls.TryGetValue(fileName, out RagdollParams ragdoll))
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
                        selectedFile = GetDefaultFile(speciesName);
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
                DebugConsole.Log($"[RagdollParams] Loading ragdoll from {selectedFile}.");
                T r = new T();
                if (r.Load(selectedFile, speciesName))
                {
                    if (!ragdolls.ContainsKey(r.Name))
                    {
                        ragdolls.Add(r.Name, r);
                    }
                    return r;
                }
                else
                {
                    DebugConsole.ThrowError($"[RagdollParams] Failed to load ragdoll {r} at {selectedFile} for the character {speciesName}. Creating a dummy file.");
                    var defaultFile = GetDefaultFile(speciesName);
                    if (File.Exists(defaultFile))
                    {
                        DebugConsole.ThrowError($"[RagdollParams] Renaming the invalid file as {selectedFile}.invalid");
                        // Rename the old file so that it's not lost.
                        File.Move(defaultFile, defaultFile + ".invalid");
                    }
                    return CreateDefault<T>(defaultFile, speciesName, dummyParams);
                }
            }
            return (T)ragdoll;
        }

        /// <summary>
        /// Creates a default ragdoll for the species using a predefined configuration.
        /// Note: Use only to create ragdolls for new characters, because this overrides the old ragdoll!
        /// </summary>
        public static T CreateDefault<T>(string fullPath, string speciesName, params object[] ragdollConfig) where T : RagdollParams, new()
        {
            // Remove the old ragdolls, if found.
            if (allRagdolls.ContainsKey(speciesName))
            {
                DebugConsole.NewMessage($"[RagdollParams] Removing the old ragdolls from {speciesName}.", Color.Red);
                allRagdolls.Remove(speciesName);
            }
            var ragdolls = new Dictionary<string, RagdollParams>();
            allRagdolls.Add(speciesName, ragdolls);
            var instance = new T();
            XElement ragdollElement = new XElement("Ragdoll", ragdollConfig);
            instance.doc = new XDocument(ragdollElement);
            instance.UpdatePath(fullPath);
            instance.IsLoaded = instance.Deserialize(ragdollElement);
            instance.Save();
            instance.Load(fullPath, speciesName);
            ragdolls.Add(instance.Name, instance);
            DebugConsole.NewMessage("[RagdollParams] New default ragdoll params successfully created at " + fullPath, Color.NavajoWhite);
            return instance as T;
        }

        protected override void UpdatePath(string fullPath)
        {
            if (SpeciesName == null)
            {
                base.UpdatePath(fullPath);
            }
            else
            {
                // Update the key by removing and re-adding the ragdoll.
                if (allRagdolls.TryGetValue(SpeciesName, out Dictionary<string, RagdollParams> ragdolls))
                {
                    ragdolls.Remove(Name);
                }
                base.UpdatePath(fullPath);
                if (ragdolls != null)
                {
                    if (!ragdolls.ContainsKey(Name))
                    {
                        ragdolls.Add(Name, this);
                    }
                }
            }
        }

        public bool Save(string fileNameWithoutExtension = null)
        {
            OriginalElement = MainElement;
            GetAllSubParams().ForEach(p => p.SetCurrentElementAsOriginalElement());
            Serialize();
            return base.Save(fileNameWithoutExtension, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = false
            });
        }

        protected bool Load(string file, string speciesName)
        {
            if (Load(file))
            {
                SpeciesName = speciesName;
                CreateColliders();
                CreateLimbs();
                CreateJoints();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Applies the current properties to the xml definition without saving to file.
        /// </summary>
        public void Apply()
        {
            Serialize();
        }

        /// <summary>
        /// Resets the current properties to the xml (stored in memory). Force reload reloads the file from disk.
        /// </summary>
        public override bool Reset(bool forceReload = false)
        {
            if (forceReload)
            {
                return Load(FullPath, SpeciesName);
            }
            Deserialize(OriginalElement, recursive: true);
            GetAllSubParams().ForEach(sp => sp.Reset());
            return true;
        }

        protected void CreateColliders()
        {
            ColliderParams.Clear();
            for (int i = 0; i < MainElement.Elements("collider").Count(); i++)
            {
                var element = MainElement.Elements("collider").ElementAt(i);
                string name = i > 0 ? "Secondary Collider" : "Main Collider";
                ColliderParams.Add(new ColliderParams(element, this, name));
            }
        }

        protected void CreateLimbs()
        {
            Limbs.Clear();
            foreach (var element in MainElement.Elements("limb"))
            {
                Limbs.Add(new LimbParams(element, this));
            }
            Limbs = Limbs.OrderBy(l => l.ID).ToList();
        }

        protected void CreateJoints()
        {
            Joints.Clear();
            foreach (var element in MainElement.Elements("joint"))
            {
                Joints.Add(new JointParams(element, this));
            }
        }

        protected bool Deserialize(XElement element = null, bool recursive = true)
        {
            if (base.Deserialize(element))
            {
                if (recursive)
                {
                    GetAllSubParams().ForEach(p => p.Deserialize());
                }
                return true;
            }
            return false;
        }

        protected bool Serialize(XElement element = null, bool recursive = true)
        {
            if (base.Serialize(element))
            {
                if (recursive)
                {
                    GetAllSubParams().ForEach(p => p.Serialize());
                }
                return true;
            }
            return false;
        }

#if CLIENT
        public void AddToEditor(ParamsEditor editor, bool alsoChildren = true, int space = 0)
        {
            base.AddToEditor(editor);
            if (alsoChildren)
            {
                var subParams = GetAllSubParams();
                foreach (var subParam in subParams)
                {
                    subParam.AddToEditor(editor, true, space);
                }
            }
            if (space > 0)
            {
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: new Color(20, 20, 20, 255))
                {
                    CanBeFocused = false
                };
            }
        }
#endif

        #region Memento
        public override void CreateSnapshot()
        {
            Serialize();
            if (doc == null)
            {
                DebugConsole.ThrowError("[RagdollParams] The source XML Document is null!");
                return;
            }
            var copy = new RagdollParams
            {
                IsLoaded = true,
                doc = new XDocument(doc)
            };
            copy.CreateColliders();
            copy.CreateLimbs();
            copy.CreateJoints();
            copy.Deserialize();
            copy.Serialize();
            memento.Store(copy);
        }
        public override void Undo() => RevertTo(memento.Undo() as RagdollParams);
        public override void Redo() => RevertTo(memento.Redo() as RagdollParams);

        private void RevertTo(RagdollParams source)
        {
            if (source.MainElement == null)
            {
                DebugConsole.ThrowError("[RagdollParams] The source XML Element of the given RagdollParams is null!");
                return;
            }
            Deserialize(source.MainElement, recursive: false);
            var sourceSubParams = source.GetAllSubParams().ToList();
            var subParams = GetAllSubParams().ToList();
            // TODO: cannot currently undo joint/limb deletion.
            if (sourceSubParams.Count != subParams.Count)
            {
                DebugConsole.ThrowError("[RagdollParams] The count of the sub params differs! Failed to revert to the previous snapshot! Please reset the ragdoll to undo the changes.");
                return;
            }
            for (int i = 0; i < subParams.Count; i++)
            {
                var subSubParams = subParams[i].SubParams;
                if (subSubParams.Count != sourceSubParams[i].SubParams.Count)
                {
                    DebugConsole.ThrowError("[RagdollParams] The count of the sub sub params differs! Failed to revert to the previous snapshot! Please reset the ragdoll to undo the changes.");
                    return;
                }
                subParams[i].Deserialize(sourceSubParams[i].Element, recursive: false);
                for (int j = 0; j < subSubParams.Count; j++)
                {
                    subSubParams[j].Deserialize(sourceSubParams[i].SubParams[j].Element, recursive: false);
                    // Since we cannot use recursion here, we have to go deeper manually, if necessary.
                }
            }
        }
        #endregion
    }
}