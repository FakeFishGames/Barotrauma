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
    class DurationListElement
    {
        public StatusEffect Parent;
        public Entity Entity;
        public List<ISerializableEntity> Targets;
        public float StartTimer;
    }
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
            if (property.GetValue() == null)
            {
                DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "- property.GetValue() returns null!!");
                return false;
            }
            Type type = property.GetValue().GetType();

            float? floatValue = null;
            float? floatProperty = null;
            if (type == typeof(float) || type == typeof(int))
            {
                floatValue = Convert.ToSingle(Value);
                floatProperty = Convert.ToSingle(property.GetValue());
            }

            switch (Operator)
            {
                case "==":
                    if (property.GetValue().Equals(floatValue == null ? Value : floatValue))
                        return true;
                    break;
                case "!=":
                    if (property.GetValue().Equals(floatValue == null ? Value : floatValue))
                        return true;
                    break;
                case ">":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty > floatValue)
                        return true;
                    break;
                case "<":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty < floatValue)
                        return true;
                    break;
                case ">=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty >= floatValue)
                        return true;
                    break;
                case "<=":
                    if (floatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + Value.ToString() + " (" + Value.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty <= floatValue)
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
        private HashSet<string> tags;
        
        private readonly float duration;
        public static List<DurationListElement> DurationList = new List<DurationListElement>();

        public bool CheckConditionalAlways; //Always do the conditional checks for the duration/delay. If false, only check conditional on apply.

        public bool Stackable; //Can the same status effect be applied several times to the same targets?

        private readonly int useItemCount;
        private readonly int cancelStatusEffect;

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

        public string Tags
        {
            get { return string.Join(",", tags); }
            set
            {
                tags.Clear();
                if (value == null) return;

                string[] newTags = value.Split(',');
                foreach (string tag in newTags)
                {
                    string newTag = tag.Trim();
                    if (!tags.Contains(newTag)) tags.Add(newTag);
                }

            }
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
            tags = new HashSet<string>(element.GetAttributeString("tags", "").Split(','));

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
                    case "stackable":
                        Stackable = attribute.GetAttributeBool(true);
                        break;
                    case "checkconditionalalways":
                        CheckConditionalAlways = attribute.GetAttributeBool(false);
                        break;
                    case "sound":
                        DebugConsole.ThrowError("Error in StatusEffect " + element.Parent.Name.ToString() +
                            " - sounds should be defined as child elements of the StatusEffect, not as attributes.");
                        break;
                    default:
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
                        useItemCount++;
                        break;
                    case "cancel":
                    case "cancelstatuseffect":
                        //This only works if there's a conditional checking for status effect tags. There is no way to cancel *all* status effects atm.
                        cancelStatusEffect = 1;
                        if (subElement.GetAttributeBool("all", false) == true)
                            cancelStatusEffect = 2;
                        break;
                    case "requireditem":
                    case "requireditems":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement);

                        if (newRequiredItem == null) continue;
                        
                        requiredItems.Add(newRequiredItem);
                        break;
                    case "conditional":
                        IEnumerable<XAttribute> conditionalAttributes = subElement.Attributes();
                        foreach(XAttribute attribute in conditionalAttributes)
                        {
                            string attributeString = XMLExtensions.GetAttributeObject(attribute).ToString();
                            string atStr = attributeString;
                            string[] splitString = atStr.Split(' ');
                            string op = splitString[0];
                            if (splitString.Length > 0)
                            {
                                for (int i=1; i<splitString.Length; i++)
                                {
                                    atStr = splitString[i] + (i > 1 && i < splitString.Length ? " " : "");
                                }
                            }
                            //thanks xml for not letting me use < or > in attributes :(
                            switch (op)
                            {
                                case "e":
                                case "eq":
                                case "equals":
                                    op = "==";
                                    break;
                                case "ne":
                                case "neq":
                                case "notequals":
                                case "!":
                                case "!e":
                                case "!eq":
                                case "!equals":
                                    op = "!=";
                                    break;
                                case "gt":
                                case "greaterthan":
                                    op = ">";
                                    break;
                                case "lt":
                                case "lessthan":
                                    op = "<";
                                    break;
                                case "gte":
                                case "gteq":
                                case "greaterthanequals":
                                    op = ">=";
                                    break;
                                case "lte":
                                case "lteq":
                                case "lessthanequals":
                                    op = "<=";
                                    break;
                                default:
                                    if (op != "==" && op != "!=" && op != ">" && op != "<" && op != ">=" && op != "<=") //Didn't use escape strings or anything
                                    {
                                        atStr = attributeString; //We probably don't even have an operator
                                        op = "==";
                                    }
                                    break;
                            }
                            propertyConditionals.Add(new PropertyConditional(attribute.Name.ToString().ToLowerInvariant(), op, atStr));
                        }
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
                    if (target == null || target.SerializableProperties == null) continue;

                    if (!target.SerializableProperties.TryGetValue(pc.Attribute, out SerializableProperty property))
                    {
                        //Do special conditional checks

                        string valStr = pc.Value.ToString();
                        if (pc.Attribute == "name")
                            return pc.Operator == "==" ? target.Name == valStr : target.Name != valStr;

                        if (pc.Attribute == "speciesname" && target is Character)
                            return pc.Operator == "==" ? ((Character)target).SpeciesName == valStr : ((Character)target).SpeciesName != valStr;

                        if ((pc.Attribute == "hastag" || pc.Attribute == "hastags") && target is Item)
                        {
                            string[] readTags = valStr.Split(',');
                            int matches = 0;
                            foreach (string tag in readTags)
                                if (((Item)target).HasTag(tag)) matches++;

                            //If operator is == then it needs to match everything, otherwise if its != there must be zero matches.
                            return pc.Operator == "==" ? matches >= readTags.Length : matches <= 0;
                        }

                        List<DurationListElement> durations = DurationList.FindAll(d => d.Targets.Contains(target));
                        List<DelayedListElement> delays = DelayedEffect.DelayList.FindAll(d => d.Targets.Contains(target));

                        bool success = false;
                        if (pc.Attribute == "hasstatustag" || pc.Attribute == "hasstatustags" && (durations.Any() || delays.Any()))
                        {
                            string[] readTags = valStr.Split(',');
                            foreach (DurationListElement duration in durations)
                            {
                                int matches = 0;
                                foreach (string tag in readTags)
                                    if (duration.Parent.HasTag(tag)) matches++;

                                success = pc.Operator == "==" ? matches >= readTags.Length : matches <= 0;
                                if (cancelStatusEffect > 0 && success)
                                    DurationList.Remove(duration);
                                if (cancelStatusEffect != 2) //cancelStatusEffect 1 = only cancel once, cancelStatusEffect 2 = cancel all of matching tags
                                    return success;
                            }
                            foreach (DelayedListElement delay in delays)
                            {
                                int matches = 0;
                                foreach (string tag in readTags)
                                    if (delay.Parent.HasTag(tag)) matches++;

                                success = pc.Operator == "==" ? matches >= readTags.Length : matches <= 0;
                                if (cancelStatusEffect > 0 && success)
                                    DelayedEffect.DelayList.Remove(delay);
                                if (cancelStatusEffect != 2) //ditto
                                    return success;
                            }
                        }
                        return success;
                    }
                    else if (!pc.Matches(property))
                        return false;
                }
            }
            return true;
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;

            if (targetNames != null && !targetNames.Contains(target.Name)) return;

            if (duration > 0.0f && !Stackable && DurationList.Find(d => d.Parent == this && d.Entity == entity && d.Targets.Contains(target)) != null) return;

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

            if (useItemCount > 0)
            {
                for (int i=0; i<useItemCount; i++)
                {
                    foreach (Item item in targets.FindAll(t => t is Item).Cast<Item>())
                    {
                        item.Use(deltaTime, targets.FirstOrDefault(t => t is Character) as Character);
                    }
                }
            }

            if (duration > 0.0f)
            {
                DurationListElement element = new DurationListElement();
                element.Parent = this;
                element.StartTimer = duration;
                element.Entity = entity;
                element.Targets = targets;

                DurationList.Add(element);
            }
            else
            {
                foreach (ISerializableEntity target in targets)
                {
                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        SerializableProperty property;

                        if (target == null || target.SerializableProperties == null || !target.SerializableProperties.TryGetValue(propertyNames[i], out property)) continue;

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
            for (int i = DurationList.Count - 1; i >= 0; i--)
            {
                DurationListElement element = DurationList[i];

                if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
                {
                    DurationList.Remove(element);
                    continue;
                }

                foreach (ISerializableEntity target in element.Targets)
                {
                    for (int n = 0; n < element.Parent.propertyNames.Length; n++)
                    {
                        SerializableProperty property;

                        if (target == null || target.SerializableProperties == null || !target.SerializableProperties.TryGetValue(element.Parent.propertyNames[n], out property)) continue;

                        element.Parent.ApplyToProperty(property, element.Parent.propertyEffects[n], CoroutineManager.UnscaledDeltaTime);
                    }
                }

                element.StartTimer -= deltaTime;

                if (element.StartTimer > 0.0f) continue;
                DurationList.Remove(element);
            }
        }

        public static void StopAll()
        {
            CoroutineManager.StopCoroutines("statuseffect");
        }

        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) return;
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) return true;

            return (tags.Contains(tag) || tags.Contains(tag.ToLowerInvariant()));
        }
    }
}
