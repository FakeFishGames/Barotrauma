using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma
{
    partial class PropertyConditional
    {
        public string Attribute;
        public string Operator;
        public object Value;

        public PropertyConditional(string Attribute, string Operator, object Value)
        {
            this.Attribute = Attribute;
            this.Operator = Operator;
            this.Value = Value;
        }

        public bool Matches(SerializableProperty property)
        {
            Type type = Value.GetType();
            if (property.GetValue() == null)
            {
                DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + type + ") to property \"" + property.Name + "- property.GetValue() returns null!!");
                return false;
            }

            float? floatValue = null;
            if ((type == typeof(float) || type == typeof(int)) && (property.GetValue() is float || property.GetValue() is int))
            {
                floatValue = Convert.ToSingle(Value);
            }

            switch (Operator)
            {
                case "==":
                    if (property.GetValue().Equals(Value))
                        return true;
                    break;
                case "!=":
                    if (!property.GetValue().Equals(Value))
                        return true;
                    break;
                case ">":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue().GetType() + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if ((float)property.GetValue() > floatValue)
                        return true;
                    break;
                case "<":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue().GetType() + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if ((float)property.GetValue() < floatValue)
                        return true;
                    break;
                case ">=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue().GetType() + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if ((float)property.GetValue() >= floatValue)
                        return true;
                    break;
                case "<=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue().GetType() + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if ((float)property.GetValue() <= floatValue)
                        return true;
                    break;
            }
            return false;
        }
    }
    partial class StatusEffect
    {
        [Flags]
        public enum TargetType
        {
            This = 1, Parent = 2, Character = 4, Contained = 8, Nearby = 16, UseTarget = 32, Hull = 64
        }

        private TargetType targetTypes;
        private HashSet<string> targetNames;

        private List<RelatedItem> requiredItems;

#if CLIENT
        private List<ParticleEmitter> particleEmitters;

        private Sound sound;
        private bool loopSound;
#endif

        public string[] propertyNames;
        private object[] propertyEffects;

        List<PropertyConditional> propertyConditionals;

        private bool setValue;
        
        private bool disableDeltaTime;

        private HashSet<string> onContainingNames;
        
        private readonly float duration;

        private readonly bool useItem;

        public readonly ActionType type;

        private Explosion explosion;

        public readonly float FireSize;

        public TargetType Targets
        {
            get { return targetTypes; }
        }

        public HashSet<string> TargetNames
        {
            get { return targetNames; }
        }

        public HashSet<string> OnContainingNames
        {
            get { return onContainingNames; }
        }

        public static StatusEffect Load(XElement element)
        {
            if (element.Attribute("delay")!=null)
            {
                return new DelayedEffect(element);
            }
                
            return new StatusEffect(element);
        }
                
        protected StatusEffect(XElement element)
        {
            requiredItems = new List<RelatedItem>();

#if CLIENT
            particleEmitters = new List<ParticleEmitter>();
#endif

            IEnumerable<XAttribute> attributes = element.Attributes();            
            List<XAttribute> propertyAttributes = new List<XAttribute>();
            propertyConditionals = new List<PropertyConditional>();

            foreach (XAttribute attribute in attributes)
            {
                switch (attribute.Name.ToString())
                {
                    case "type":
                        try
                        {
                            type = (ActionType)Enum.Parse(typeof(ActionType), attribute.Value, true);
                        }

                        catch
                        {
                            string[] split = attribute.Value.Split('=');
                            type = (ActionType)Enum.Parse(typeof(ActionType), split[0], true);

                            string[] containingNames = split[1].Split(',');
                            onContainingNames = new HashSet<string>();
                            for (int i = 0; i < containingNames.Length; i++)
                            {
                                onContainingNames.Add(containingNames[i].Trim());
                            }
                        }

                        break;
                    case "target":
                        string[] Flags = attribute.Value.Split(',');
                        foreach (string s in Flags)
                        {
                            targetTypes |= (TargetType)Enum.Parse(typeof(TargetType), s, true);
                        }

                        break;
                    case "disabledeltatime":                        
                        disableDeltaTime = attribute.GetAttributeBool(false);
                        break;
                    case "setvalue":                        
                        setValue = attribute.GetAttributeBool(false);
                        break;
                    case "targetnames":
                        string[] names = attribute.Value.Split(',');
                        targetNames = new HashSet<string>();
                        for (int i=0; i < names.Length; i++ )
                        {
                            targetNames.Add(names[i].Trim());
                        }
                        break;
                    case "duration":
                        duration = attribute.GetAttributeFloat(0.0f);
                        break;
                    case "sound":
                        DebugConsole.ThrowError("Error in StatusEffect " + element.Parent.Name.ToString() +
                            " - sounds should be defined as child elements of the StatusEffect, not as attributes.");
                        break;
                    default:
                        object attributeObject = XMLExtensions.GetAttributeObject(attribute);
                        string attributeString = attributeObject.ToString();
                        Type type = attributeObject.GetType();

                        string op = attributeString.Substring(0, Math.Min(2, attributeString.Length));
                        if (op != "!=" && op != ">=" && op != "<=" && op != "==" && (op.StartsWith(">") || op.StartsWith("<")))
                            op = op.Substring(0, 1);
                        
                        if (op == "!=" || op == ">=" || op == "<=" || op == "==" || op == ">" || op == "<") //Oh shit this is a conditional!
                        {
                            attributeObject = attributeString.Substring(op.Length, attributeString.Length);
                            propertyConditionals.Add(new PropertyConditional(attribute.Name.ToString().ToLowerInvariant(), op, attributeObject));
                        }
                        else
                            propertyAttributes.Add(attribute);
                        break;
                }
            }

            int count = propertyAttributes.Count;
            propertyNames = new string[count];
            propertyEffects = new object[count];

            int n = 0;
            foreach (XAttribute attribute in propertyAttributes)
            {
                propertyNames[n] = attribute.Name.ToString().ToLowerInvariant();
                propertyEffects[n] = XMLExtensions.GetAttributeObject(attribute);
                n++;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "explosion":
                        explosion = new Explosion(subElement);
                        break;
                    case "fire":
                        FireSize = subElement.GetAttributeFloat("size",10.0f);
                        break;
                    case "use":
                    case "useitem":
                        useItem = true;
                        break;
                    case "requireditem":
                    case "requireditems":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement);

                        if (newRequiredItem == null) continue;
                        
                        requiredItems.Add(newRequiredItem);
                        break;
#if CLIENT
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                    case "sound":
                        sound = Sound.Load(subElement);
                        loopSound = subElement.GetAttributeBool("loop", false);
                        break;
#endif
                }
            }
        }

        public virtual bool HasRequiredItems(Entity entity)
        {
            if (requiredItems == null) return true;
            foreach (RelatedItem requiredItem in requiredItems)
            {
                Item item = entity as Item;
                if (item != null)
                {
                    if (!requiredItem.CheckRequirements(null, item)) return false;
                }
                Character character = entity as Character;
                if (character != null)
                {
                    if (!requiredItem.CheckRequirements(character, null)) return false;
                }
            }
            return true;
        }

        public virtual bool HasRequiredConditions(List<ISerializableEntity> targets)
        {
            if (!propertyConditionals.Any()) return true;
            foreach (ISerializableEntity target in targets)
            {
                foreach (PropertyConditional pc in propertyConditionals)
                {
                    string op = pc.Operator;
                    object value = pc.Value;

                    SerializableProperty property;

                    if (target == null || target.SerializableProperties == null || !target.SerializableProperties.TryGetValue(pc.Attribute, out property)) continue;

                    if (!pc.Matches(property))
                        return false;
                }
            }
            return true;
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;

            if (targetNames != null && !targetNames.Contains(target.Name)) return;

            List<ISerializableEntity> targets = new List<ISerializableEntity>();
            targets.Add(target);

            if (!HasRequiredConditions(targets)) return;

            Apply(type, deltaTime, entity, targets);
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, List<ISerializableEntity> targets)
        {
            if (this.type != type || !HasRequiredItems(entity) || !HasRequiredConditions(targets)) return;

            Apply(deltaTime, entity, targets);
        }

        protected void Apply(float deltaTime, Entity entity, List<ISerializableEntity> targets)
        {
#if CLIENT
            if (sound != null)
            {
                if (loopSound)
                {
                    if (!Sounds.SoundManager.IsPlaying(sound))
                    {
                        sound.Play(entity.WorldPosition);
                    }
                    else
                    {
                        sound.UpdatePosition(entity.WorldPosition);
                    }
                }
                else
                {
                    sound.Play(entity.WorldPosition);
                }
            }
#endif

            if (useItem)
            {
                foreach (Item item in targets.FindAll(t => t is Item).Cast<Item>())
                {
                    item.Use(deltaTime, targets.FirstOrDefault(t => t is Character) as Character);
                }
            }

            foreach (ISerializableEntity target in targets)
            {
                for (int i = 0; i < propertyNames.Length; i++)
                {
                    SerializableProperty property;

                    if (target == null || target.SerializableProperties == null || !target.SerializableProperties.TryGetValue(propertyNames[i], out property)) continue;

                    if (duration > 0.0f)
                    {
                        CoroutineManager.StartCoroutine(
                            ApplyToPropertyOverDuration(duration, property, propertyEffects[i]), "statuseffect");

                    }
                    else
                    {
                        ApplyToProperty(property, propertyEffects[i], deltaTime);                          
                    }
                }
            }

            if (explosion != null) explosion.Explode(entity.WorldPosition);

            
            Hull hull = null;
            if (entity is Character) 
            {
                hull = ((Character)entity).AnimController.CurrentHull;
            }
            else if (entity is Item)
            {
                hull = ((Item)entity).CurrentHull;
            }

            if (FireSize > 0.0f)
            {
                var fire = new FireSource(entity.WorldPosition, hull);

                fire.Size = new Vector2(FireSize, fire.Size.Y);
            }

#if CLIENT
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(deltaTime, entity.WorldPosition, hull);
            }
#endif
        }

        private IEnumerable<object> ApplyToPropertyOverDuration(float duration, SerializableProperty property, object value)
        {
            float timer = duration;
            while (timer > 0.0f)
            {
                ApplyToProperty(property, value, CoroutineManager.UnscaledDeltaTime);

                timer -= CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            yield return CoroutineStatus.Success;
        }

        private void ApplyToProperty(SerializableProperty property, object value, float deltaTime)
        {
            if (disableDeltaTime || setValue) deltaTime = 1.0f;

            Type type = value.GetType();
            if (type == typeof(float) ||
                (type == typeof(int) && property.GetValue() is float))
            {
                float floatValue = Convert.ToSingle(value) * deltaTime;
                
                if (!setValue) floatValue += (float)property.GetValue();
                property.TrySetValue(floatValue);
            }
            else if (type == typeof(int) && value is int)
            {
                int intValue = (int)((int)value * deltaTime);
                if (!setValue) intValue += (int)property.GetValue();
                property.TrySetValue(intValue);
            }
            else if (type == typeof(bool) && value is bool)
            {
                property.TrySetValue((bool)value);
            }
            else if (type == typeof(string))
            {
                property.TrySetValue((string)value);
            }
            else
            {
                DebugConsole.ThrowError("Couldn't apply value " + value.ToString() + " (" + type + ") to property \"" + property.Name + "\" (" + property.GetValue().GetType() + ")! "
                    + "Make sure the type of the value set in the config files matches the type of the property.");
            }
        }

        public static void UpdateAll(float deltaTime)
        {
            DelayedEffect.Update(deltaTime);
        }

        public static void StopAll()
        {
            CoroutineManager.StopCoroutines("statuseffect");
        }
    }
}
