using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml;
using System.Linq;
using Barotrauma.Extensions;

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

        [Serialize(100f, true), Editable]
        public float Noise { get; set; }

        [Serialize("", true), Editable]
        public string BloodDecal { get; private set; }

        public readonly string File;

        public List<CharacterSubParams> SubParams { get; private set; } = new List<CharacterSubParams>();
        public HealthParams Health { get; private set; }
        public AIParams AI { get; private set; }

        public CharacterParams(string file)
        {
            File = file;
            Load();
        }

        protected override string GetName() => "Character Config File";

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

            // TODO: bloodemitter, gibemitter, sounds, inventory
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
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: new Color(20, 20, 20, 255))
                {
                    CanBeFocused = false
                };
            }
        }
#endif
    }

    class HealthParams : CharacterSubParams
    {
        public override string Name => "Health";

        [Serialize(100f, true), Editable]
        public float Vitality { get; set; }

        [Serialize(true, true), Editable]
        public bool DoesBleed { get; set; }

        [Serialize(float.NegativeInfinity, true), Editable]
        public float CrushDepth { get; set; }

        // Make editable?
        [Serialize(false, true)]
        public bool UseHealthWindow { get; set; }

        // TODO: limbhealths, sprite?

        public HealthParams(XElement element, CharacterParams character) : base(element, character) { }
    }

    class TargetParams : CharacterSubParams
    {
        public override string Name => "Target";

        [Serialize("", true), Editable]
        public string Tag { get; private set; }

        [Serialize(AIState.Idle, true), Editable]
        public AIState State { get; set; }

        [Serialize(0f, true), Editable]
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

    class AIParams : CharacterSubParams
    {
        public override string Name => "AI";

        [Serialize(1.0f, true), Editable]
        public float CombatStrength { get; private set; }

        [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2f)]
        public float Sight { get; private set; }

        [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2f)]
        public float Hearing { get; private set; }

        [Serialize(100f, true), Editable]
        public float AggressionHurt { get; private set; }

        [Serialize(10f, true), Editable]
        public float AggressionGreed { get; private set; }

        [Serialize(0f, true), Editable]
        public float FleeHealthThreshold { get; private set; }

        [Serialize(false, true), Editable]
        public bool AttackOnlyWhenProvoked { get; private set; }

        [Serialize(false, true), Editable]
        public bool AggressiveBoarding { get; private set; }

        // TODO: latchonto, swarming

        public IEnumerable<TargetParams> Targets => targets;
        protected readonly List<TargetParams> targets = new List<TargetParams>();

        public AIParams(XElement element, CharacterParams character) : base(element, character)
        {
            element.GetChildElements("target").ForEach(t => TryAddTarget(t, character, out _));
            element.GetChildElements("targetpriority").ForEach(t => TryAddTarget(t, character, out _));
        }

#if CLIENT
        public override void AddToEditor(ParamsEditor editor, bool recursive = true, int space = 0, ScalableFont titleFont = null)
        {
            base.AddToEditor(editor, recursive, 0, titleFont);
            var buttonParent = new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, 40), editor.EditorBox.Content.RectTransform), style: null, color: new Color(20, 20, 20, 255))
            {
                CanBeFocused = false
            };
            new GUIButton(new RectTransform(new Vector2(0.45f, 0.8f), buttonParent.RectTransform, Anchor.CenterLeft), "Add New Target")
            {
                OnClicked = (button, data) =>
                {
                    TryAddEmptyTarget(out _);
                    buttonParent.SetAsLastChild();
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.45f, 0.8f), buttonParent.RectTransform, Anchor.CenterRight), "Remove Last Target")
            {
                OnClicked = (button, data) =>
                {
                    TryRemoveLastTarget();
                    return true;
                }
            };
            if (space > 0)
            {
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: new Color(20, 20, 20, 255))
                {
                    CanBeFocused = false
                };
            }
        }
#endif

        private bool TryAddTarget(XElement targetElement, CharacterParams character, out TargetParams target)
        {
            target = null;
            string tag = targetElement.GetAttributeString("tag", null);
            if (tag == null) { return false; }
            tag = tag.ToLowerInvariant();
            if (targets.Any(t => t.Tag == tag))
            {
                DebugConsole.ThrowError($"Multiple targets with the same tag ('{tag}') defined! Only the first will be used!");
                return false;
            }
            else
            {
                target = new TargetParams(targetElement, character);
                targets.Add(target);
                SubParams.Add(target);
                return true;
            }
        }

        public bool TryAddEmptyTarget(out TargetParams targetParams) => TryAddNewTarget("newtarget" + targets.Count, AIState.Attack, 0f, out targetParams);

        public bool TryAddNewTarget(string tag, AIState state, float priority, out TargetParams targetParams, bool createNewElement = true)
        {
            var element = TargetParams.CreateNewElement(tag, state, priority);
            if (TryAddTarget(element, Character, out targetParams))
            {
                Element.Add(element);
#if CLIENT
                targetParams.AddToEditor(ParamsEditor.Instance, titleFont: GUI.SmallFont);
#endif
            }
            return targetParams != null;
        }

        public bool TryRemoveLastTarget()
        {
            if (targets.None()) { return false; }
            var last = targets.LastOrDefault();
            if (last == null) { return false; }
            targets.Remove(last);
            SubParams.Remove(last);
            last.Element.Remove();
#if CLIENT
            last.SerializableEntityEditor.RectTransform.Parent = null;
#endif
            return true;
        }

        public bool TryGetTarget(string targetTag, out TargetParams target)
        {
            target = targets.FirstOrDefault(t => t.Tag == targetTag);
            return target != null;
        }

        public TargetParams GetTarget(string targetTag)
        {
            if (!TryGetTarget(targetTag, out TargetParams target))
            {
                DebugConsole.ThrowError($"Cannot find a target with the tag {targetTag}!");
            }
            return target;
        }
    }

    abstract class CharacterSubParams : ISerializableEntity
    {
        public virtual string Name { get; set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
        public XElement Element { get; set; }
        public List<CharacterSubParams> SubParams { get; set; } = new List<CharacterSubParams>();

        public CharacterParams Character { get; private set; }

        public CharacterSubParams(XElement element, CharacterParams character)
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
}
