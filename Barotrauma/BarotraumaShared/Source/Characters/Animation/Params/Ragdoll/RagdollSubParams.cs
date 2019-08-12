using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.SpriteDeformations;
#endif

namespace Barotrauma
{
    class JointParams : RagdollSubParams
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

    class LimbParams : RagdollSubParams
    {
        public readonly SpriteParams normalSpriteParams;
        public readonly SpriteParams damagedSpriteParams;
        public readonly SpriteParams deformSpriteParams;

        // TODO: support for multiple attacks?
        public LimbAttackParams Attack { get; private set; }
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

        /// <summary>
        /// Note that editing this in-game doesn't currently have any effect (unless the ragdoll is recreated). It should be visible, but readonly in the editor.
        /// </summary>
        [Serialize(-1, true), Editable]
        public int ID { get; set; }

        [Serialize(LimbType.None, true), Editable]
        public LimbType Type { get; set; }

        [Serialize(true, true), Editable]
        public bool Flip { get; set; }

        [Serialize(0, true), Editable]
        public int HealthIndex { get; set; }

        [Serialize(0f, true), Editable(ToolTip = "Higher values make AI characters prefer attacking this limb.")]
        public float AttackPriority { get; set; }

        [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 500)]
        public float SteerForce { get; set; }

        [Serialize("0, 0", true), Editable(ToolTip = "Only applicable if this limb is a foot. Determines the \"neutral position\" of the foot relative to a joint determined by the \"RefJoint\" parameter. For example, a value of {-100, 0} would mean that the foot is positioned on the floor, 100 units behind the reference joint.")]
        public Vector2 StepOffset { get; set; }

        [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
        public float Radius { get; set; }

        [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
        public float Height { get; set; }

        [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 1000)]
        public float Width { get; set; }

        [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 10000)]
        public float Mass { get; set; }

        [Serialize(10f, true), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float Density { get; set; }

        [Serialize("0, 0", true), Editable(ToolTip = "The position which is used to lead the IK chain to the IK goal. Only applicable if the limb is hand or foot.")]
        public Vector2 PullPos { get; set; }

        [Serialize(-1, true), Editable(ToolTip = "Only applicable if this limb is a foot. Determines which joint is used as the \"neutral x-position\" for the foot movement. For example in the case of a humanoid-shaped characters this would usually be the waist. The position can be offset using the StepOffset parameter.")]
        public int RefJoint { get; set; }

        [Serialize(false, true), Editable]
        public bool IgnoreCollisions { get; set; }

        [Serialize("", true), Editable]
        public string Notes { get; set; }

        // Non-editable ->
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
                deformSpriteParams = new SpriteParams(deformSpriteElement, ragdoll)
                {
                    Deformation = new LimbDeformationParams(deformSpriteElement, ragdoll)
                };
                deformSpriteParams.SubParams.Add(deformSpriteParams.Deformation);
                SubParams.Add(deformSpriteParams);
            }
            var attackElement = element.GetChildElement("attack");
            if (attackElement != null)
            {
                Attack = new LimbAttackParams(attackElement, ragdoll);
                SubParams.Add(Attack);
            }
            foreach (var damageElement in element.GetChildElements("damagemodifier"))
            {
                var damageModifier = new DamageModifierParams(damageElement, ragdoll);
                DamageModifiers.Add(damageModifier);
                SubParams.Add(damageModifier);
            }
        }

        public bool AddAttack()
        {
            if (Attack != null) { return false; }
            var element = new XElement("attack");
            Element.Add(element);
            Attack = new LimbAttackParams(element, Ragdoll);
            SubParams.Add(Attack);
            return Attack != null;
        }

        public bool RemoveAttack()
        {
            if (Attack == null) { return false; }
            Attack.Element.Remove();
            SubParams.Remove(Attack);
            Attack = null;
            return Attack == null;
        }

        public bool AddNewDamageModifier()
        {
            Serialize();
            var subElement = new XElement("damagemodifier");
            Element.Add(subElement);
            var damageModifier = new DamageModifierParams(subElement, Ragdoll);
            DamageModifiers.Add(damageModifier);
            SubParams.Add(damageModifier);
            Serialize();
            return true;
        }

        public bool RemoveDamageModifier(DamageModifierParams damageModifier)
        {
            if (!DamageModifiers.Contains(damageModifier)) { return false; }
            Serialize();
            SubParams.Remove(damageModifier);
            DamageModifiers.Remove(damageModifier);
            damageModifier.Element.Remove();
            return Serialize();
        }

        public bool RemoveLastDamageModifier()
        {
            var last = DamageModifiers.LastOrDefault();
            if (last == null) { return false; }
            return RemoveDamageModifier(last);
        }
    }

    class SpriteParams : RagdollSubParams
    {
        [Serialize("0, 0, 0, 0", true), Editable]
        public Rectangle SourceRect { get; set; }

        [Serialize("0.5, 0.5", true), Editable(DecimalCount = 2, ToolTip = "Relative to the collider.")]
        public Vector2 Origin { get; set; }

        [Serialize(0f, true), Editable(DecimalCount = 3)]
        public float Depth { get; set; }

        [Serialize("", true)]
        public string Texture { get; set; }

        public LimbDeformationParams Deformation { get; set; }

        public SpriteParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll) { }
    }

    class LimbDeformationParams : RagdollSubParams
    {
        public LimbDeformationParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
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

    class ColliderParams : RagdollSubParams
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

    // TODO: conditionals?
    class LimbAttackParams : RagdollSubParams
    {
        public Attack Attack { get; private set; }

        public LimbAttackParams(XElement element, RagdollParams ragdoll) : base(element, ragdoll)
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

        public bool RemoveLastAffliction()
        {
            var afflictions = Element.GetChildElements("affliction");
            var last = afflictions.LastOrDefault();
            if (last == null) { return false; }
            return RemoveAffliction(last);
        }
    }

    class DamageModifierParams : RagdollSubParams
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

    abstract class RagdollSubParams : ISerializableEntity
    {
        public virtual string Name { get; set; }
        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
        public XElement Element { get; set; }
        public XElement OriginalElement { get; protected set; }
        public List<RagdollSubParams> SubParams { get; set; } = new List<RagdollSubParams>();
        public RagdollParams Ragdoll { get; private set; }

        public virtual string GenerateName() => Element.Name.ToString();

        public RagdollSubParams(XElement element, RagdollParams ragdoll)
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
        public Dictionary<Affliction, SerializableEntityEditor> AfflictionEditors { get; private set; } = new Dictionary<Affliction, SerializableEntityEditor>();
        public virtual void AddToEditor(ParamsEditor editor, bool recursive = true, int space = 0)
        {
            SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, inGame: false, showName: true, titleFont: GUI.LargeFont);
            if (this is SpriteParams spriteParams && spriteParams.Deformation != null)
            {
                foreach (var deformation in spriteParams.Deformation.Deformations.Keys)
                {
                    new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, deformation, inGame: false, showName: true);
                }
            }
            if (this is LimbAttackParams attackParams)
            {
                SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, attackParams.Attack, inGame: false, showName: true);
                AfflictionEditors.Clear();
                foreach (var affliction in attackParams.Attack.Afflictions.Keys)
                {
                    var afflictionEditor = new SerializableEntityEditor(SerializableEntityEditor.RectTransform, affliction, inGame: false, showName: true);
                    AfflictionEditors.Add(affliction, afflictionEditor);
                    SerializableEntityEditor.AddCustomContent(afflictionEditor, SerializableEntityEditor.ContentCount);
                }
            }
            else if (this is DamageModifierParams damageModifierParams)
            {
                SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, damageModifierParams.DamageModifier, inGame: false, showName: true);
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
}
