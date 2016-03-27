using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class StatusEffect
    {
        [Flags]
        public enum TargetType
        {
            This = 1, Parent = 2, Character = 4, Contained = 8, Nearby = 16, UseTarget = 32, Hull = 64
        }
        
        private TargetType targetTypes;
        private string[] targetNames;

        private List<RelatedItem> requiredItems;

        public string[] propertyNames;
        private object[] propertyEffects;

        private bool setValue;
        
        private bool disableDeltaTime;
        
        private string[] onContainingNames;
        
        private readonly float duration;

        private readonly bool useItem;

        public readonly ActionType type;

        private Explosion explosion;

        public readonly float FireSize;

        private Sound sound;
        
        public TargetType Targets
        {
            get { return targetTypes; }
        }

        public string[] TargetNames
        {
            get { return targetNames; }
        }

        public string[] OnContainingNames
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

            IEnumerable<XAttribute> attributes = element.Attributes();            
            List<XAttribute> propertyAttributes = new List<XAttribute>();
            
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
                            onContainingNames = new string[containingNames.Count()];
                            for (int i =0; i < containingNames.Count(); i++)
                            {
                                onContainingNames[i] = containingNames[i].Trim();
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
                        disableDeltaTime = ToolBox.GetAttributeBool(attribute, false);
                        break;
                    case "setvalue":                        
                        setValue = ToolBox.GetAttributeBool(attribute, false);
                        break;
                    case "targetnames":
                        string[] names = attribute.Value.Split(',');
                        targetNames = new string[names.Count()];
                        for (int i=0; i < names.Count(); i++ )
                        {
                            targetNames[i] = names[i].Trim();
                        }
                        break;
                    case "sound":
                        sound = Sound.Load(attribute.Value.ToString());
                        break;
                    case "duration":
                        duration = ToolBox.GetAttributeFloat(attribute, 0.0f);
                        break;
                    default:
                        propertyAttributes.Add(attribute);
                        break;
                }
            }

            int count = propertyAttributes.Count();
            propertyNames = new string[count];
            propertyEffects = new object[count];

            int n = 0;
            foreach (XAttribute attribute in propertyAttributes)
            {
                propertyNames[n] = attribute.Name.ToString().ToLower();
                propertyEffects[n] = ToolBox.GetAttributeObject(attribute);
                n++;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "explosion":
                        explosion = new Explosion(subElement);
                        break;
                    case "fire":
                        FireSize = ToolBox.GetAttributeFloat(subElement,"size",10.0f);
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
                }
            }

            //oxygen = ToolBox.GetAttributeFloat(element, "oxygen", 0.0f);

            
            //deteriorateOnActive = ToolBox.GetAttributeFloat(element, "deteriorateonactive", 0.0f);
            //deteriorateOnUse = ToolBox.GetAttributeFloat(element, "deteriorateonuse", 0.0f);
        }


        //public virtual void Apply(ActionType type, float deltaTime, Item item, Character Character = null)
        //{
        //    if (this.type == type) Apply(deltaTime, Character, item);
        //}

        private bool HasRequiredItems(Entity entity)
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

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, IPropertyObject target)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;

            if (targetNames != null && !targetNames.Contains(target.Name)) return;

            List<IPropertyObject> targets = new List<IPropertyObject>();
            targets.Add(target);

            Apply(deltaTime, entity, targets);
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, List<IPropertyObject> targets)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;

            Apply(deltaTime, entity, targets);
        }

        protected void Apply(float deltaTime, Entity entity, List<IPropertyObject> targets)
        {

            if (explosion != null) explosion.Explode(entity.WorldPosition);
            
            if (FireSize > 0.0f)
            {
                var fire = new FireSource(entity.WorldPosition);
                
                fire.Size = new Vector2(FireSize, fire.Size.Y);
            }            

            if (sound != null) sound.Play(1.0f, 1000.0f, entity.WorldPosition);

            if (useItem)
            {
                foreach (Item item in targets.FindAll(t => t is Item).Cast<Item>())
                {
                    item.Use(deltaTime, targets.FirstOrDefault(t => t is Character) as Character);
                }
            }

            foreach (IPropertyObject target in targets)
            {
                for (int i = 0; i < propertyNames.Count(); i++)
                {
                    ObjectProperty property;

                    //if (targetNames!=null && !targetNames.Contains(target.Name)) continue;

                    if (!target.ObjectProperties.TryGetValue(propertyNames[i], out property)) continue;

                    if (duration > 0.0f)
                    {
                        CoroutineManager.StartCoroutine(ApplyToPropertyOverDuration(duration, property, propertyEffects[i]));
                    }
                    else
                    {
                        ApplyToProperty(property, propertyEffects[i], deltaTime);                          
                    }
                }

            }
        }

        private IEnumerable<object> ApplyToPropertyOverDuration(float duration, ObjectProperty property, object value)
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

        private void ApplyToProperty(ObjectProperty property, object value, float deltaTime)
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
                DebugConsole.ThrowError("Couldn't apply value "+value.ToString()+" ("+type+") to property ''"+property.Name+"'' ("+property.GetValue().GetType()+")! "
                    +"Make sure the type of the value set in the config files matches the type of the property.");
            }
        }

        public static void UpdateAll(float deltaTime)
        {
            for (int i = DelayedEffect.List.Count-1; i>= 0; i--)
            {
                DelayedEffect.List[i].Update(deltaTime);
            }
        }        
    }
}
