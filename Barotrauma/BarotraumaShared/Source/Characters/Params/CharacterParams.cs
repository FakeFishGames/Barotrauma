using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml;
using System.Linq;
using Barotrauma.Extensions;
#if CLIENT
using SoundType = Barotrauma.CharacterSound.SoundType;
#endif

namespace Barotrauma
{
    /// <summary>
    /// Contains character data that should be editable in the character editor.
    /// </summary>
    class CharacterParams : EditableParams
    {
        [Serialize("", true), Editable]
        public string SpeciesName { get; private set; }

        [Serialize(false, true), Editable]
        public bool Humanoid { get; private set; }

        [Serialize(false, true), Editable]
        public bool Husk { get; private set; }

        [Serialize(false, true), Editable]
        public bool NeedsAir { get; set; }

        [Serialize(false, true), Editable]
        public bool CanSpeak { get; set; }

        [Serialize(100f, true), Editable(minValue: 0f, maxValue: 1000f, ToolTip = "How much noise the character makes when moving?")]
        public float Noise { get; set; }

        [Serialize("", true), Editable]
        public string BloodDecal { get; private set; }

        public readonly string File;

        public readonly List<SubParam> SubParams = new List<SubParam>();
        public readonly List<SoundParams> Sounds = new List<SoundParams>();
        public readonly List<ParticleParams> BloodEmitters = new List<ParticleParams>();
        public readonly List<ParticleParams> GibEmitters = new List<ParticleParams>();
        public readonly List<InventoryParams> Inventories = new List<InventoryParams>();
        public HealthParams Health { get; private set; }
        public AIParams AI { get; private set; }

        public CharacterParams(string file)
        {
            File = file;
            Load();
        }

        protected override string GetName() => "Character Config File";

        public override XElement MainElement => doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;

        public bool Load()
        {
            bool success= base.Load(File);
            CreateSubParams();
            return success;
        }

        public bool Save(string fileNameWithoutExtension = null)
        {
            Serialize();
            return base.Save(fileNameWithoutExtension, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = false
            });
        }

        public override bool Reset(bool forceReload = false)
        {
            if (forceReload)
            {
                return Load();
            }
            Deserialize(OriginalElement, alsoChildren: true);
            SubParams.ForEach(sp => sp.Reset());
            return true;
        }

        protected void CreateSubParams()
        {
            SubParams.Clear();
            var health = MainElement.GetChildElement("health");
            if (health != null)
            {
                Health = new HealthParams(health, this);
                SubParams.Add(Health);
            }
            // TODO: support for multiple ai elements?
            var ai = MainElement.GetChildElement("ai");
            if (ai != null)
            {
                AI = new AIParams(ai, this);
                SubParams.Add(AI);
            }
            foreach (var element in MainElement.GetChildElements("bloodemitter"))
            {
                var emitter = new ParticleParams(element, this);
                BloodEmitters.Add(emitter);
                SubParams.Add(emitter);
            }
            foreach (var element in MainElement.GetChildElements("gibemitter"))
            {
                var emitter = new ParticleParams(element, this);
                GibEmitters.Add(emitter);
                SubParams.Add(emitter);
            }
            foreach (var soundElement in MainElement.GetChildElements("sound"))
            {
                var sound = new SoundParams(soundElement, this);
                Sounds.Add(sound);
                SubParams.Add(sound);
            }
            foreach (var inventoryElement in MainElement.GetChildElements("inventory"))
            {
                var inventory = new InventoryParams(inventoryElement, this);
                Inventories.Add(inventory);
                SubParams.Add(inventory);
            }
        }

        protected bool Deserialize(XElement element = null, bool alsoChildren = true, bool recursive = true)
        {
            if (base.Deserialize(element))
            {
                if (alsoChildren)
                {
                    SubParams.ForEach(p => p.Deserialize(recursive));
                }
                return true;
            }
            return false;
        }

        protected bool Serialize(XElement element = null, bool alsoChildren = true, bool recursive = true)
        {
            if (base.Serialize(element))
            {
                if (alsoChildren)
                {
                    SubParams.ForEach(p => p.Serialize(recursive));
                }
                return true;
            }
            return false;
        }

#if CLIENT
        public void AddToEditor(ParamsEditor editor, bool alsoChildren = true, bool recursive = true, int space = 0)
        {
            base.AddToEditor(editor);
            if (alsoChildren)
            {
                SubParams.ForEach(s => s.AddToEditor(editor, recursive));
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

        public bool AddSound() => TryAddSubParam(new XElement("sound"), (e, c) => new SoundParams(e, c), out _, Sounds);

        public void AddInventory() => TryAddSubParam(new XElement("inventory", new XElement("item")), (e, c) => new InventoryParams(e, c), out _, Inventories);

        public void AddBloodEmitter() => AddEmitter("bloodemitter");
        public void AddGibEmitter() => AddEmitter("gibemitter");

        private void AddEmitter(string type)
        {
            switch (type)
            {
                case "gibemitter":
                    TryAddSubParam(new XElement(type), (e, c) => new ParticleParams(e, c), out _, GibEmitters);
                    break;
                case "bloodemitter":
                    TryAddSubParam(new XElement(type), (e, c) => new ParticleParams(e, c), out _, BloodEmitters);
                    break;
                default: throw new NotImplementedException(type);
            }
        }

        public bool RemoveSound(SoundParams soundParams) => RemoveSubParam(soundParams);
        public bool RemoveBloodEmitter(ParticleParams emitter) => RemoveSubParam(emitter, BloodEmitters);
        public bool RemoveGibEmitter(ParticleParams emitter) => RemoveSubParam(emitter, GibEmitters);
        public bool RemoveInventory(InventoryParams inventory) => RemoveSubParam(inventory, Inventories);

        protected bool RemoveSubParam<T>(T subParam, IList<T> collection = null) where T : SubParam
        {
            if (subParam == null || subParam.Element == null || subParam.Element.Parent == null) { return false; }
            if (collection != null && !collection.Contains(subParam)) { return false; }
            if (!SubParams.Contains(subParam)) { return false; }
            collection?.Remove(subParam);
            SubParams.Remove(subParam);
            subParam.Element.Remove();
            return  true;
        }

        protected bool TryAddSubParam<T>(XElement element, Func<XElement, CharacterParams, T> constructor, out T subParam, IList<T> collection = null, Func<IList<T>, bool> filter = null) where T : SubParam
        {
            subParam = constructor(element, this);
            if (collection != null && filter != null)
            {
                if (filter(collection)) { return false; }
            }
            MainElement.Add(element);
            SubParams.Add(subParam);
            collection?.Add(subParam);
            return subParam != null;
        }

        #region Subparams
        public class SoundParams : SubParam
        {
            public override string Name => "Sound";

            [Serialize("", true), Editable]
            public string File { get; private set; }

#if CLIENT
            [Serialize(SoundType.Idle, true), Editable]
            public SoundType State { get; private set; }
#endif

            [Serialize(1000f, true), Editable(minValue: 0f, maxValue: 10000f)]
            public float Range { get; private set; }

            [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2.0f)]
            public float Volume { get; private set; }

            [Serialize(Gender.None, true), Editable(ToolTip = "Is the sound gender specific?")]
            public Gender Gender { get; private set; }

            public SoundParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class ParticleParams : SubParam
        {
            private string name;
            public override string Name
            {
                get
                {
                    if (name == null && Element != null)
                    {
                        name = Element.Name.ToString().FormatCamelCaseWithSpaces();
                    }
                    return name;
                }
            }

            [Serialize("", true), Editable]
            public string Particle { get; set; }

            [Serialize(0f, true), Editable(-360f, 360f, decimals: 0)]
            public float AngleMin { get; private set; }

            [Serialize(0f, true), Editable(-360f, 360f, decimals: 0)]
            public float AngleMax { get; private set; }

            [Serialize(1.0f, true), Editable(0f, 100f, decimals: 2)]
            public float ScaleMin { get; private set; }

            [Serialize(1.0f, true), Editable(0f, 100f, decimals: 2)]
            public float ScaleMax { get; private set; }

            [Serialize(0f, true), Editable(0f, 10000f, decimals: 0)]
            public float VelocityMin { get; private set; }

            [Serialize(0f, true), Editable(0f, 10000f, decimals: 0)]
            public float VelocityMax { get; private set; }

            [Serialize(0f, true), Editable(0f, 100f, decimals: 2)]
            public float EmitInterval { get; private set; }

            [Serialize(0, true), Editable(0, 1000)]
            public int ParticlesPerSecond { get; private set; }

            [Serialize(0, true), Editable(0, 1000)]
            public int ParticleAmount { get; private set; }

            [Serialize(false, true), Editable]
            public bool HighQualityCollisionDetection { get; private set; }

            [Serialize(false, true), Editable]
            public bool CopyEntityAngle { get; private set; }

            public ParticleParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class HealthParams : SubParam
        {
            public override string Name => "Health";

            [Serialize(100f, true), Editable(minValue: 1, maxValue: 10000f, ToolTip = "How much (max) health does the character have?")]
            public float Vitality { get; set; }

            [Serialize(true, true), Editable]
            public bool DoesBleed { get; set; }

            [Serialize(float.NegativeInfinity, true), Editable(minValue: float.NegativeInfinity, maxValue: 0)]
            public float CrushDepth { get; set; }

            // Make editable?
            [Serialize(false, true)]
            public bool UseHealthWindow { get; set; }

            // TODO: limbhealths, sprite?

            public HealthParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class InventoryParams : SubParam
        {
            public class InventoryItem : SubParam
            {
                public override string Name => "Item";

                [Serialize("", true), Editable(ToolTip = "Item identifier.")]
                public string Identifier { get; private set; }

                public InventoryItem(XElement element, CharacterParams character) : base(element, character) { }
            }

            public override string Name => "Inventory";

            [Serialize("Any, Any", true), Editable(ToolTip = "Which slots the inventory holds? Accepted types: None, Any, RightHand, LeftHand, Head, InnerClothes, OuterClothes, Headset, and Card.")]
            public string Slots { get; private set; }

            [Serialize(false, true), Editable]
            public bool AccessibleWhenAlive { get; private set; }

            [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 1.0f, ToolTip = "What are the odds that this inventory is spawned on the character?")]
            public float Commonness { get; private set; }

            public List<InventoryItem> Items { get; private set; } = new List<InventoryItem>();

            public InventoryParams(XElement element, CharacterParams character) : base(element, character)
            {
                foreach (var itemElement in element.GetChildElements("item"))
                {
                    var item = new InventoryItem(itemElement, character);
                    SubParams.Add(item);
                    Items.Add(item);
                }
            }

            public void AddItem(string identifier = null)
            {
                identifier = identifier ?? "";
                var element = new XElement("item", new XAttribute("identifier", identifier));
                Element.Add(element);
                var item = new InventoryItem(element, Character);
                SubParams.Add(item);
                Items.Add(item);
            }

            public bool RemoveItem(InventoryItem item) => RemoveSubParam(item, Items);
        }

        public class AIParams : SubParam
        {
            public override string Name => "AI";

            [Serialize(1.0f, true), Editable(ToolTip = "How strong other characters think this character is? Only affects AI.")]
            public float CombatStrength { get; private set; }

            [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2f, ToolTip = "Affects how far the character can see the targets. Used as a multiplier.")]
            public float Sight { get; private set; }

            [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2f, ToolTip = "Affects how far the character can hear the targets. Used as a multiplier.")]
            public float Hearing { get; private set; }

            [Serialize(100f, true), Editable(minValue: -1000f, maxValue: 1000f, ToolTip = "How much the target priority increase when the character takes damage? Additive.")]
            public float AggressionHurt { get; private set; }

            [Serialize(10f, true), Editable(minValue: 0f, maxValue: 1000f, ToolTip = "How much the target priority increase when the character takes damage? Additive.")]
            public float AggressionGreed { get; private set; }

            [Serialize(0f, true), Editable(minValue: 0f, maxValue: 100f, ToolTip = "If the health drops below this threshold, the character flees. In percentages.")]
            public float FleeHealthThreshold { get; private set; }

            [Serialize(false, true), Editable(ToolTip = "Does the character attack ONLY when provoked?")]
            public bool AttackOnlyWhenProvoked { get; private set; }

            [Serialize(false, true), Editable(ToolTip = "Does the character try to break inside the sub?")]
            public bool AggressiveBoarding { get; private set; }

            // TODO: latchonto, swarming

            public IEnumerable<TargetParams> Targets => targets;
            protected readonly List<TargetParams> targets = new List<TargetParams>();

            public AIParams(XElement element, CharacterParams character) : base(element, character)
            {
                element.GetChildElements("target").ForEach(t => TryAddTarget(t, out _));
                element.GetChildElements("targetpriority").ForEach(t => TryAddTarget(t, out _));
            }

            private bool TryAddTarget(XElement targetElement, out TargetParams target)
            {
                string tag = targetElement.GetAttributeString("tag", null);
                if (!CheckTag(tag))
                {
                    target = null;
                    DebugConsole.ThrowError($"Multiple targets with the same tag ('{tag}') defined! Only the first will be used!");
                    return false;
                }
                else
                {
                    target = new TargetParams(targetElement, Character);
                    targets.Add(target);
                    SubParams.Add(target);
                    return true;
                }
            }

            public bool TryAddEmptyTarget(out TargetParams targetParams) => TryAddNewTarget("newtarget" + targets.Count, AIState.Attack, 0f, out targetParams);

            public bool TryAddNewTarget(string tag, AIState state, float priority, out TargetParams targetParams)
            {
                var element = TargetParams.CreateNewElement(tag, state, priority);
                if (TryAddTarget(element, out targetParams))
                {
                    Element.Add(element);
                }
                return targetParams != null;
            }

            private bool CheckTag(string tag)
            {
                if (tag == null) { return false; }
                tag = tag.ToLowerInvariant();
                return targets.None(t => t.Tag == tag);
            }

            public bool RemoveTarget(TargetParams target) => RemoveSubParam(target, targets);

            public bool TryGetTarget(string targetTag, out TargetParams target)
            {
                target = targets.FirstOrDefault(t => t.Tag == targetTag);
                return target != null;
            }

            public TargetParams GetTarget(string targetTag, bool throwError = true)
            {
                if (!TryGetTarget(targetTag, out TargetParams target))
                {
                    if (throwError)
                    {
                        DebugConsole.ThrowError($"Cannot find a target with the tag {targetTag}!");
                    }
                }
                return target;
            }
        }

        public class TargetParams : SubParam
        {
            public override string Name => "Target";

            [Serialize("", true), Editable(ToolTip = "Can be an item tag, species name or something else. Examples: decoy, provocative, light, dead, human, crawler, wall, nasonov, sonar, door, stronger, weaker, light, human, room...")]
            public string Tag { get; private set; }

            [Serialize(AIState.Idle, true), Editable]
            public AIState State { get; set; }

            [Serialize(0f, true), Editable(minValue: 0f, maxValue: 1000f, ToolTip = "What base priority is given to the target?")]
            public float Priority { get; set; }

            public TargetParams(XElement element, CharacterParams character) : base(element, character) { }

            public TargetParams(string tag, AIState state, float priority, CharacterParams character) : base(CreateNewElement(tag, state, priority), character) { }

            public static XElement CreateNewElement(string tag, AIState state, float priority)
            {
                return new XElement("target",
                            new XAttribute("tag", tag),
                            new XAttribute("state", state),
                            new XAttribute("priority", priority));
            }
        }

        public abstract class SubParam : ISerializableEntity
        {
            public virtual string Name { get; set; }
            public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
            public XElement Element { get; set; }
            public List<SubParam> SubParams { get; set; } = new List<SubParam>();

            public CharacterParams Character { get; private set; }

            public SubParam(XElement element, CharacterParams character)
            {
                Element = element;
                Character = character;
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            }

            public virtual bool Deserialize(bool recursive = true)
            {
                SerializableProperties = SerializableProperty.DeserializeProperties(this, Element);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Deserialize(true));
                }
                return SerializableProperties != null;
            }

            public virtual bool Serialize(bool recursive = true)
            {
                SerializableProperty.SerializeProperties(this, Element, true);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Serialize(true));
                }
                return true;
            }

            public virtual void Reset()
            {
                // Don't use recursion, because the reset method might be overriden
                Deserialize(false);
                SubParams.ForEach(sp => sp.Reset());
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

#if CLIENT
            public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
            public virtual void AddToEditor(ParamsEditor editor, bool recursive = true, int space = 0, ScalableFont titleFont = null)
            {
                SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, inGame: false, showName: true, titleFont: titleFont ?? GUI.LargeFont);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.AddToEditor(editor, true, titleFont: titleFont ?? GUI.SmallFont));
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
