using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System.IO;
using System.Xml;
using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.SpriteDeformations;
#endif

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

    class RagdollParams : EditableParams, IMemorizable<RagdollParams>
    {
        #region Ragdoll
        public const float MIN_SCALE = 0.1f;
        public const float MAX_SCALE = 2;

        public string SpeciesName { get; private set; }

        [Serialize("", true, description: "Default path for the limb sprite textures. Used only if the limb specific path for the limb is not defined"), Editable]
        public string Texture { get; set; }

        [Serialize(0.0f, true, description: "The orientation of the sprites as drawn on the sprite sheet. Can be overridden by setting a value for Limb's 'Sprite Orientation'. Used mainly for animations and widgets."), Editable(-360, 360)]
        public float SpritesheetOrientation { get; set; }

        public bool IsSpritesheetOrientationHorizontal
        {
            get
            {
                return 
                    (SpritesheetOrientation > 45.0f && SpritesheetOrientation < 135.0f) ||
                    (SpritesheetOrientation > 255.0f && SpritesheetOrientation < 315.0f);
            }
        }

        private float limbScale;
        [Serialize(1.0f, true), Editable(MIN_SCALE, MAX_SCALE, DecimalCount = 3)]
        public float LimbScale { get { return limbScale; } set { limbScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); } }

        private float jointScale;
        [Serialize(1.0f, true), Editable(MIN_SCALE, MAX_SCALE, DecimalCount = 3)]
        public float JointScale { get { return jointScale; } set { jointScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); } }

        // Don't show in the editor, because shouldn't be edited in runtime.  Requires that the limb scale and the collider sizes are adjusted. TODO: automatize?
        [Serialize(1f, false)]
        public float TextureScale { get; set; }

        [Serialize(45f, true, description: "How high from the ground the main collider levitates when the character is standing? Doesn't affect swimming."), Editable(0f, 1000f)]
        public float ColliderHeightFromFloor { get; set; }

        [Serialize(50f, true, description: "How much impact is required before the character takes impact damage?"), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
        public float ImpactTolerance { get; set; }

        [Serialize(true, true, description: "Can the creature enter submarine. Creatures that cannot enter submarines, always collide with it, even when there is a gap."), Editable()]
        public bool CanEnterSubmarine { get; set; }

        [Serialize(true, true), Editable]
        public bool CanWalk { get; set; }

        [Serialize(true, true, description: "Can the character be dragged around by other creatures?"), Editable()]
        public bool Draggable { get; set; }

        [Serialize(LimbType.Torso, true), Editable]
        public LimbType MainLimb { get; set; }

        private static Dictionary<string, Dictionary<string, RagdollParams>> allRagdolls = new Dictionary<string, Dictionary<string, RagdollParams>>();

        public List<ColliderParams> Colliders { get; private set; } = new List<ColliderParams>();
        public List<LimbParams> Limbs { get; private set; } = new List<LimbParams>();
        public List<JointParams> Joints { get; private set; } = new List<JointParams>();

        protected IEnumerable<SubParam> GetAllSubParams() =>
            Colliders.Select(c => c as SubParam)
            .Concat(Limbs.Select(j => j as SubParam)
            .Concat(Joints.Select(j => j as SubParam)));

        public static string GetDefaultFileName(string speciesName) => $"{speciesName.CapitaliseFirstInvariant()}DefaultRagdoll";
        public static string GetDefaultFile(string speciesName, ContentPackage contentPackage = null)
            => Path.Combine(GetFolder(speciesName, contentPackage), $"{GetDefaultFileName(speciesName)}.xml");

        public static string GetFolder(string speciesName, ContentPackage contentPackage = null)
        {
            CharacterPrefab prefab = CharacterPrefab.Find(p => p.Identifier.Equals(speciesName, StringComparison.OrdinalIgnoreCase) && (contentPackage == null || p.ContentPackage == contentPackage));
            if (prefab?.XDocument == null)
            {
                DebugConsole.ThrowError($"Failed to find config file for '{speciesName}' (content package {contentPackage?.Name ?? "null"})");
                return string.Empty;
            }
            return GetFolder(prefab.XDocument, prefab.FilePath);
        }

        public static string GetFolder(XDocument doc, string filePath)
        {
            var folder = doc.Root?.Element("ragdolls")?.GetAttributeString("folder", string.Empty);
            if (string.IsNullOrEmpty(folder) || folder.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                folder = Path.Combine(Path.GetDirectoryName(filePath), "Ragdolls") + Path.DirectorySeparatorChar;
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
            if (string.IsNullOrWhiteSpace(speciesName))
            {
                throw new Exception($"Species name null or empty!");
            }
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
                        selectedFile = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
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
                   // Failing to create a ragdoll causes so many issues that cannot be handled. Dummy ragdoll just seems to make things harded to debug. It's better to fail early.
                   throw new Exception($"[RagdollParams] Failed to load ragdoll {r.Name} from {selectedFile} for the character {speciesName}.");
                }
            }
            return (T)ragdoll;
        }

        /// <summary>
        /// Creates a default ragdoll for the species using a predefined configuration.
        /// Note: Use only to create ragdolls for new characters, because this overrides the old ragdoll!
        /// </summary>
        public static T CreateDefault<T>(string fullPath, string speciesName, XElement mainElement) where T : RagdollParams, new()
        {
            // Remove the old ragdolls, if found.
            if (allRagdolls.ContainsKey(speciesName))
            {
                DebugConsole.NewMessage($"[RagdollParams] Removing the old ragdolls from {speciesName}.", Color.Red);
                allRagdolls.Remove(speciesName);
            }
            var ragdolls = new Dictionary<string, RagdollParams>();
            allRagdolls.Add(speciesName, ragdolls);
            var instance = new T
            {
                doc = new XDocument(mainElement)
            };
            instance.UpdatePath(fullPath);
            instance.IsLoaded = instance.Deserialize(mainElement);
            instance.Save();
            instance.Load(fullPath, speciesName);
            ragdolls.Add(instance.Name, instance);
            DebugConsole.NewMessage("[RagdollParams] New default ragdoll params successfully created at " + fullPath, Color.NavajoWhite);
            return instance as T;
        }

        public static void ClearCache() => allRagdolls.Clear();

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
            // Don't use recursion, because the reset method might be overriden
            Deserialize(OriginalElement, alsoChildren: false, recursive: false);
            GetAllSubParams().ForEach(sp => sp.Reset());
            return true;
        }

        protected void CreateColliders()
        {
            Colliders.Clear();
            for (int i = 0; i < MainElement.GetChildElements("collider").Count(); i++)
            {
                var element = MainElement.GetChildElements("collider").ElementAt(i);
                string name = i > 0 ? "Secondary Collider" : "Main Collider";
                Colliders.Add(new ColliderParams(element, this, name));
            }
        }

        protected void CreateLimbs()
        {
            Limbs.Clear();
            foreach (var element in MainElement.GetChildElements("limb"))
            {
                Limbs.Add(new LimbParams(element, this));
            }
            Limbs = Limbs.OrderBy(l => l.ID).ToList();
        }

        protected void CreateJoints()
        {
            Joints.Clear();
            foreach (var element in MainElement.GetChildElements("joint"))
            {
                Joints.Add(new JointParams(element, this));
            }
        }

        public bool Deserialize(XElement element = null, bool alsoChildren = true, bool recursive = true)
        {
            if (base.Deserialize(element))
            {
                if (alsoChildren)
                {
                    GetAllSubParams().ForEach(p => p.Deserialize(recursive: recursive));
                }
                return true;
            }
            return false;
        }

        public bool Serialize(XElement element = null, bool alsoChildren = true, bool recursive = true)
        {
            if (base.Serialize(element))
            {
                if (alsoChildren)
                {
                    GetAllSubParams().ForEach(p => p.Serialize(recursive: recursive));
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
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
            }
        }
#endif
        #endregion

        #region Memento
        public Memento<RagdollParams> Memento { get; protected set; } = new Memento<RagdollParams>();
        public void StoreSnapshot()
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
            Memento.Store(copy);
        }
        public void Undo() => RevertTo(Memento.Undo() as RagdollParams);
        public void Redo() => RevertTo(Memento.Redo() as RagdollParams);
        public void ClearHistory() => Memento.Clear();

        private void RevertTo(RagdollParams source)
        {
            if (source.MainElement == null)
            {
                DebugConsole.ThrowError("[RagdollParams] The source XML Element of the given RagdollParams is null!");
                return;
            }
            Deserialize(source.MainElement, alsoChildren: false);
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

        #region Subparams
        public class JointParams : SubParam
        {
            private string name;
            [Serialize("", true), Editable]
            public override string Name
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = GenerateName();
                    }
                    return name;
                }
                set
                {
                    name = value;
                }
            }

            public override string GenerateName() => $"Joint {Limb1} - {Limb2}";

            [Serialize(-1, true), Editable]
            public int Limb1 { get; set; }

            [Serialize(-1, true), Editable]
            public int Limb2 { get; set; }

            /// <summary>
            /// Should be converted to sim units.
            /// </summary>
            [Serialize("1.0, 1.0", true, description: "Local position of the joint in the Limb1."), Editable()]
            public Vector2 Limb1Anchor { get; set; }

            /// <summary>
            /// Should be converted to sim units.
            /// </summary>
            [Serialize("1.0, 1.0", true, description: "Local position of the join in the Limb2."), Editable()]
            public Vector2 Limb2Anchor { get; set; }

            [Serialize(true, true), Editable]
            public bool CanBeSevered { get; set; }

            [Serialize(true, true), Editable]
            public bool LimitEnabled { get; set; }

            /// <summary>
            /// In degrees.
            /// </summary>
            [Serialize(0f, true), Editable]
            public float UpperLimit { get; set; }

            /// <summary>
            /// In degrees.
            /// </summary>
            [Serialize(0f, true), Editable]
            public float LowerLimit { get; set; }

            [Serialize(0.25f, true), Editable]
            public float Stiffness { get; set; }

            public JointParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll) { }
        }

        public class LimbParams : SubParam
        {
            public readonly SpriteParams normalSpriteParams;
            public readonly SpriteParams damagedSpriteParams;
            public readonly DeformSpriteParams deformSpriteParams;
            public readonly List<DecorativeSpriteParams> decorativeSpriteParams = new List<DecorativeSpriteParams>();

            public AttackParams Attack { get; private set; }
            public SoundParams Sound { get; private set; }
            public LightSourceParams LightSource { get; private set; }
            public List<DamageModifierParams> DamageModifiers { get; private set; } = new List<DamageModifierParams>();

            private string name;
            [Serialize("", true), Editable]
            public override string Name
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = GenerateName();
                    }
                    return name;
                }
                set
                {
                    name = value;
                }
            }

            public override string GenerateName() => $"Limb {ID}";

            public SpriteParams GetSprite() => deformSpriteParams ?? normalSpriteParams;

            [Serialize(-1, true), Editable(ReadOnly = true)]
            public int ID { get; set; }

            [Serialize(LimbType.None, true, description: "The limb type affects many things, like the animations. Torso or Head are considered as the main limbs. Every character should have at least one Torso or Head."), Editable()]
            public LimbType Type { get; set; }

            [Serialize(float.NaN, true, description: "The orientation of the sprite as drawn on the sprite sheet. Overrides the value defined in the Ragdoll settings. Used mainly for animations and widgets."), Editable(-360, 360)]
            public float SpriteOrientation { get; set; }

            /// <summary>
            /// The orientation of the sprite as drawn on the sprite sheet (in radians).
            /// </summary>
            public float GetSpriteOrientation() => MathHelper.ToRadians(float.IsNaN(SpriteOrientation) ? Ragdoll.SpritesheetOrientation : SpriteOrientation);

            [Serialize(true, true, description: "Does the limb flip when the character flips?"), Editable()]
            public bool Flip { get; set; }

            [Serialize(false, true, description: "Currently only works with non-deformable (normal) sprites."), Editable()]
            public bool MirrorVertically { get; set; }

            [Serialize(false, true), Editable]
            public bool MirrorHorizontally { get; set; }

            [Serialize(false, true, description: "Disable drawing for this limb."), Editable()]
            public bool Hide { get; set; }

            [Serialize(1f, true, description: "Higher values make AI characters prefer attacking this limb."), Editable(MinValueFloat = 0.1f, MaxValueFloat = 10)]
            public float AttackPriority { get; set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 500)]
            public float SteerForce { get; set; }

            [Serialize(0f, true, description: "Radius of the collider."), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Radius { get; set; }

            [Serialize(0f, true, description: "Height of the collider."), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Height { get; set; }

            [Serialize(0f, true, description: "Width of the collider."), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Width { get; set; }

            [Serialize(10f, true, description: "The more the density the heavier the limb is."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
            public float Density { get; set; }

            [Serialize(false, true), Editable]
            public bool IgnoreCollisions { get; set; }

            [Serialize(7f, true, description: "Increasing the damping makes the limb stop rotating more quickly."), Editable]
            public float AngularDamping { get; set; }

            [Serialize("0, 0", true, description: "The position which is used to lead the IK chain to the IK goal. Only applicable if the limb is hand or foot."), Editable()]
            public Vector2 PullPos { get; set; }

            [Serialize("0, 0", true, description: "Only applicable if this limb is a foot. Determines the \"neutral position\" of the foot relative to a joint determined by the \"RefJoint\" parameter. For example, a value of {-100, 0} would mean that the foot is positioned on the floor, 100 units behind the reference joint."), Editable()]
            public Vector2 StepOffset { get; set; }

            [Serialize(-1, true, description: "The id of the refecence joint. Determines which joint is used as the \"neutral x-position\" for the foot movement. For example in the case of a humanoid-shaped characters this would usually be the waist. The position can be offset using the StepOffset parameter. Only applicable if this limb is a foot."), Editable()]
            public int RefJoint { get; set; }

            [Serialize("0, 0", true, description: "Relative offset for the mouth position (starting from the center). Only applicable for LimbType.Head. Used for eating."), Editable(DecimalCount = 2, MinValueFloat = -10f, MaxValueFloat = 10f)]
            public Vector2 MouthPos { get; set; }

            [Serialize("", true), Editable]
            public string Notes { get; set; }

            // Non-editable ->
            [Serialize(0, true)]
            public int HealthIndex { get; set; }

            [Serialize(0.3f, true)]
            public float Friction { get; set; }

            [Serialize(0.05f, true)]
            public float Restitution { get; set; }

            public LimbParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
                var spriteElement = element.GetChildElement("sprite");
                if (spriteElement != null)
                {
                    normalSpriteParams = new SpriteParams(spriteElement, ragdoll);
                    SubParams.Add(normalSpriteParams);
                }
                var damagedSpriteElement = element.GetChildElement("damagedsprite");
                if (damagedSpriteElement != null)
                {
                    damagedSpriteParams = new SpriteParams(damagedSpriteElement, ragdoll);
                    // Hide the damaged sprite params in the editor for now.
                    //SubParams.Add(damagedSpriteParams);
                }
                var deformSpriteElement = element.GetChildElement("deformablesprite");
                if (deformSpriteElement != null)
                {
                    deformSpriteParams = new DeformSpriteParams(deformSpriteElement, ragdoll);
                    SubParams.Add(deformSpriteParams);
                }
                foreach (var decorativeSpriteElement in element.GetChildElements("decorativesprite"))
                {
                    var decorativeParams = new DecorativeSpriteParams(decorativeSpriteElement, ragdoll);
                    decorativeSpriteParams.Add(decorativeParams);
                    SubParams.Add(decorativeParams);
                }
                var attackElement = element.GetChildElement("attack");
                if (attackElement != null)
                {
                    Attack = new AttackParams(attackElement, ragdoll);
                    SubParams.Add(Attack);
                }
                foreach (var damageElement in element.GetChildElements("damagemodifier"))
                {
                    var damageModifier = new DamageModifierParams(damageElement, ragdoll);
                    DamageModifiers.Add(damageModifier);
                    SubParams.Add(damageModifier);
                }
                var soundElement = element.GetChildElement("sound");
                if (soundElement != null)
                {
                    Sound = new SoundParams(soundElement, ragdoll);
                    SubParams.Add(Sound);
                }
                var lightElement = element.GetChildElement("lightsource");
                if (lightElement != null)
                {
                    LightSource = new LightSourceParams(lightElement, ragdoll);
                    SubParams.Add(LightSource);
                }
            }

            public bool AddAttack()
            {
                if (Attack != null) { return false; }
                TryAddSubParam(new XElement("attack"), (e, c) => new AttackParams(e, c), out AttackParams newAttack);
                Attack = newAttack;
                return Attack != null;
            }


            public bool AddSound()
            {
                if (Sound != null) { return false; }
                TryAddSubParam(new XElement("sound"), (e, c) => new SoundParams(e, c), out SoundParams newSound);
                Sound = newSound;
                return Sound != null;
            }

            public bool AddLight()
            {
                if (LightSource != null) { return false; }
                var lightSourceElement = new XElement("lightsource",
                    new XElement("lighttexture", new XAttribute("texture", "Content/Lights/light.png")));
                TryAddSubParam(lightSourceElement, (e, c) => new LightSourceParams(e, c), out LightSourceParams newLightSource);
                LightSource = newLightSource;
                return LightSource != null;
            }

            public bool AddDamageModifier() => TryAddSubParam(new XElement("damagemodifier"), (e, c) => new DamageModifierParams(e, c), out _, DamageModifiers);

            public bool RemoveAttack()
            {
                if (RemoveSubParam(Attack))
                {
                    Attack = null;
                    return true;
                }
                return false;
            }

            public bool RemoveSound()
            {
                if (RemoveSubParam(Sound))
                {
                    Sound = null;
                    return true;
                }
                return false;
            }

            public bool RemoveLight()
            {
                if (RemoveSubParam(LightSource))
                {
                    LightSource = null;
                    return true;
                }
                return false;
            }

            public bool RemoveDamageModifier(DamageModifierParams damageModifier) => RemoveSubParam(damageModifier, DamageModifiers);

            protected bool TryAddSubParam<T>(XElement element, Func<XElement, RagdollParams, T> constructor, out T subParam, IList<T> collection = null, Func<IList<T>, bool> filter = null) where T : SubParam
            {
                subParam = constructor(element, Ragdoll);
                if (collection != null && filter != null)
                {
                    if (filter(collection)) { return false; }
                }
                Element.Add(element);
                SubParams.Add(subParam);
                collection?.Add(subParam);
                return subParam != null;
            }

            protected bool RemoveSubParam<T>(T subParam, IList<T> collection = null) where T : SubParam
            {
                if (subParam == null || subParam.Element == null || subParam.Element.Parent == null) { return false; }
                if (collection != null && !collection.Contains(subParam)) { return false; }
                if (!SubParams.Contains(subParam)) { return false; }
                collection?.Remove(subParam);
                SubParams.Remove(subParam);
                subParam.Element.Remove();
                return true;
            }
        }

        public class DecorativeSpriteParams : SpriteParams
        {
            public DecorativeSpriteParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
#if CLIENT
                DecorativeSprite = new DecorativeSprite(element);
#endif
            }

#if CLIENT
            public DecorativeSprite DecorativeSprite { get; private set; }

            public override bool Deserialize(XElement element = null, bool recursive = true)
            {
                base.Deserialize(element, recursive);
                DecorativeSprite.SerializableProperties = SerializableProperty.DeserializeProperties(DecorativeSprite, element ?? Element);
                return SerializableProperties != null;
            }

            public override bool Serialize(XElement element = null, bool recursive = true)
            {
                base.Serialize(element, recursive);
                SerializableProperty.SerializeProperties(DecorativeSprite, element ?? Element);
                return true;
            }

            public override void Reset()
            {
                base.Reset();
                DecorativeSprite.SerializableProperties = SerializableProperty.DeserializeProperties(DecorativeSprite, OriginalElement);
            }
#endif
        }

        public class DeformSpriteParams : SpriteParams
        {
            public DeformationParams Deformation { get; private set; }

            public DeformSpriteParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
                Deformation = new DeformationParams(element, ragdoll);
                SubParams.Add(Deformation);
            }
        }

        public class SpriteParams : SubParam
        {
            [Serialize("0, 0, 0, 0", true), Editable]
            public Rectangle SourceRect { get; set; }

            [Serialize("0.5, 0.5", true, description: "The origin of the sprite relative to the collider."), Editable(DecimalCount = 3)]
            public Vector2 Origin { get; set; }

            [Serialize(0f, true, description: "The Z-depth of the limb relative to other limbs of the same character. 1 is front, 0 is behind."), Editable(MinValueFloat = 0, MaxValueFloat = 1, DecimalCount = 3)]
            public float Depth { get; set; }

            [Serialize("", true), Editable()]
            public string Texture { get; set; }

            public override string Name => "Sprite";

            public SpriteParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll) { }

            public string GetTexturePath() => string.IsNullOrWhiteSpace(Texture) ? Ragdoll.Texture : Texture;
        }

        public class DeformationParams : SubParam
        {
            public DeformationParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
#if CLIENT
                Deformations = new Dictionary<SpriteDeformationParams, XElement>();
                foreach (var deformationElement in element.GetChildElements("spritedeformation"))
                {
                    string typeName = deformationElement.GetAttributeString("typename", null) ?? deformationElement.GetAttributeString("type", "");
                    SpriteDeformationParams deformation = null;
                    switch (typeName.ToLowerInvariant())
                    {
                        case "inflate":
                            deformation = new InflateParams(deformationElement);
                            break;
                        case "custom":
                            deformation = new CustomDeformationParams(deformationElement);
                            break;
                        case "noise":
                            deformation = new NoiseDeformationParams(deformationElement);
                            break;
                        case "jointbend":
                        case "bendjoint":
                            deformation = new JointBendDeformationParams(deformationElement);
                            break;
                        case "reacttotriggerers":
                            deformation = new PositionalDeformationParams(deformationElement);
                            break;
                        default:
                            DebugConsole.ThrowError($"SpriteDeformationParams not implemented: '{typeName}'");
                            break;
                    }
                    if (deformation != null)
                    {
                        deformation.TypeName = typeName;
                    }
                    Deformations.Add(deformation, deformationElement);
                }
#endif
            }

#if CLIENT
            public Dictionary<SpriteDeformationParams, XElement> Deformations { get; private set; }

            public override bool Deserialize(XElement element = null, bool recursive = true)
            {
                base.Deserialize(element, recursive);
                Deformations.ForEach(d => d.Key.SerializableProperties = SerializableProperty.DeserializeProperties(d.Key, d.Value));
                return SerializableProperties != null;
            }

            public override bool Serialize(XElement element = null, bool recursive = true)
            {
                base.Serialize(element, recursive);
                Deformations.ForEach(d => SerializableProperty.SerializeProperties(d.Key, d.Value));
                return true;
            }

            public override void Reset()
            {
                base.Reset();
                Deformations.ForEach(d => d.Key.SerializableProperties = SerializableProperty.DeserializeProperties(d.Key, d.Value));
            }
#endif
        }

        public class ColliderParams : SubParam
        {
            private string name;
            [Serialize("", true), Editable]
            public override string Name
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = GenerateName();
                    }
                    return name;
                }
                set
                {
                    name = value;
                }
            }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Radius { get; set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Height { get; set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
            public float Width { get; set; }

            public ColliderParams(XElement element, RagdollParams ragdoll, string name = null) : base(element, ragdoll)
            {
                Name = name;
            }
        }

        public class LightSourceParams : SubParam
        {
            public class LightTexture : SubParam
            {
                public override string Name => "Light Texture";

                [Serialize("", true), Editable]
                public string Texture { get; private set; }

                [Serialize("0.5, 0.5", true), Editable(DecimalCount = 2)]
                public Vector2 Origin { get; set; }

                [Serialize("1.0, 1.0", true), Editable(DecimalCount = 2)]
                public Vector2 Size { get; set; }

                public LightTexture(XElement element, RagdollParams ragdoll) : base(element, ragdoll) { }
            }

            public LightTexture Texture { get; private set; }

#if CLIENT
            public Lights.LightSourceParams LightSource { get; private set; }
#endif

            public LightSourceParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
#if CLIENT
                LightSource = new Lights.LightSourceParams(element);
#endif
                var lightTextureElement = element.GetChildElement("lighttexture");
                if (lightTextureElement != null)
                {
                    Texture = new LightTexture(lightTextureElement, ragdoll);
                    SubParams.Add(Texture);
                }
            }

#if CLIENT
            public override bool Deserialize(XElement element = null, bool recursive = true)
            {
                base.Deserialize(element, recursive);
                LightSource.Deserialize(element ?? Element);
                return SerializableProperties != null;
            }

            public override bool Serialize(XElement element = null, bool recursive = true)
            {
                base.Serialize(element, recursive);
                LightSource.Serialize(element ?? Element);
                return true;
            }

            public override void Reset()
            {
                base.Reset();
                LightSource.Serialize(OriginalElement);
            }
#endif
        }

        // TODO: conditionals?
        public class AttackParams : SubParam
        {
            public Attack Attack { get; private set; }

            public AttackParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
                Attack = new Attack(element, ragdoll.SpeciesName);
            }

            public override bool Deserialize(XElement element = null, bool recursive = true)
            {
                base.Deserialize(element, recursive);
                Attack.Deserialize(element ?? Element);
                return SerializableProperties != null;
            }

            public override bool Serialize(XElement element = null, bool recursive = true)
            {
                base.Serialize(element, recursive);
                Attack.Serialize(element ?? Element);
                return true;
            }

            public override void Reset()
            {
                base.Reset();
                Attack.Deserialize(OriginalElement);
                Attack.ReloadAfflictions(OriginalElement);
            }

            public bool AddNewAffliction()
            {
                Serialize();
                var subElement = new XElement("affliction",
                    new XAttribute("identifier", "internaldamage"),
                    new XAttribute("strength", 0f),
                    new XAttribute("probability", 1.0f));
                Element.Add(subElement);
                Attack.ReloadAfflictions(Element);
                Serialize();
                return true;
            }

            public bool RemoveAffliction(XElement affliction)
            {
                Serialize();
                affliction.Remove();
                Attack.ReloadAfflictions(Element);
                return Serialize();
            }
        }

        public class DamageModifierParams : SubParam
        {
            public DamageModifier DamageModifier { get; private set; }

            public DamageModifierParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
            {
                DamageModifier = new DamageModifier(element, ragdoll.SpeciesName);
            }

            public override bool Deserialize(XElement element = null, bool recursive = true)
            {
                base.Deserialize(element, recursive);
                DamageModifier.Deserialize(element ?? Element);
                return SerializableProperties != null;
            }

            public override bool Serialize(XElement element = null, bool recursive = true)
            {
                base.Serialize(element, recursive);
                DamageModifier.Serialize(element ?? Element);
                return true;
            }

            public override void Reset()
            {
                base.Reset();
                DamageModifier.Deserialize(OriginalElement);
            }
        }

        public class SoundParams : SubParam
        {
            public override string Name => "Sound";

            [Serialize("", true), Editable]
            public string Tag { get; private set; }

            public SoundParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll) { }
        }

        public abstract class SubParam : ISerializableEntity
        {
            public virtual string Name { get; set; }
            public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
            public XElement Element { get; set; }
            public XElement OriginalElement { get; protected set; }
            public List<SubParam> SubParams { get; set; } = new List<SubParam>();
            public RagdollParams Ragdoll { get; private set; }

            public virtual string GenerateName() => Element.Name.ToString();

            public SubParam(XElement element, RagdollParams ragdoll)
            {
                Element = element;
                OriginalElement = new XElement(element);
                Ragdoll = ragdoll;
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            }

            public virtual bool Deserialize(XElement element = null, bool recursive = true)
            {
                element = element ?? Element;
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Deserialize(recursive: true));
                }
                return SerializableProperties != null;
            }

            public virtual bool Serialize(XElement element = null, bool recursive = true)
            {
                element = element ?? Element;
                SerializableProperty.SerializeProperties(this, element, true);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Serialize(recursive: true));
                }
                return true;
            }

            public virtual void SetCurrentElementAsOriginalElement()
            {
                OriginalElement = Element;
                SubParams.ForEach(sp => sp.SetCurrentElementAsOriginalElement());
            }

            public virtual void Reset()
            {
                // Don't use recursion, because the reset method might be overriden
                Deserialize(OriginalElement, false);
                SubParams.ForEach(sp => sp.Reset());
            }

#if CLIENT
            public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
            public Dictionary<Affliction, SerializableEntityEditor> AfflictionEditors { get; private set; }
            public virtual void AddToEditor(ParamsEditor editor, bool recursive = true, int space = 0)
            {
                SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, inGame: false, showName: true, titleFont: GUI.LargeFont);
                if (this is DecorativeSpriteParams decSpriteParams)
                {
                    new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, decSpriteParams.DecorativeSprite, inGame: false, showName: true, titleFont: GUI.LargeFont);
                }
                else if (this is DeformSpriteParams deformSpriteParams)
                {
                    foreach (var deformation in deformSpriteParams.Deformation.Deformations.Keys)
                    {
                        new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, deformation, inGame: false, showName: true, titleFont: GUI.LargeFont);
                    }
                }
                else if (this is AttackParams attackParams)
                {
                    SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, attackParams.Attack, inGame: false, showName: true, titleFont: GUI.LargeFont);
                    if (AfflictionEditors == null)
                    {
                        AfflictionEditors = new Dictionary<Affliction, SerializableEntityEditor>();
                    }
                    else
                    {
                        AfflictionEditors.Clear();
                    }
                    foreach (var affliction in attackParams.Attack.Afflictions.Keys)
                    {
                        var afflictionEditor = new SerializableEntityEditor(SerializableEntityEditor.RectTransform, affliction, inGame: false, showName: true);
                        AfflictionEditors.Add(affliction, afflictionEditor);
                        SerializableEntityEditor.AddCustomContent(afflictionEditor, SerializableEntityEditor.ContentCount);
                    }
                }
                else if (this is LightSourceParams lightParams)
                {
                    SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, lightParams.LightSource, inGame: false, showName: true, titleFont: GUI.LargeFont);
                }
                else if (this is DamageModifierParams damageModifierParams)
                {
                    SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, damageModifierParams.DamageModifier, inGame: false, showName: true, titleFont: GUI.LargeFont);
                }
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.AddToEditor(editor, true));
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
        }
        #endregion
    }
}