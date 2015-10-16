using FarseerPhysics;
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
            This = 1, Parent = 2, Character = 4, Contained = 8, Nearby = 16, UseTarget=32
        }

        private TargetType targetTypes;
        private string[] targetNames;

        public string[] propertyNames;
        private object[] propertyEffects;

        private bool setValue;

        private bool disableDeltaTime;
        
        private string[] onContainingNames;

        public readonly ActionType type;

        private Explosion explosion;

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
            else
            {
                return new StatusEffect(element);
            }
        }
                
        protected StatusEffect(XElement element)
        {
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
                }
            }

            //oxygen = ToolBox.GetAttributeFloat(element, "oxygen", 0.0f);

            
            //deteriorateOnActive = ToolBox.GetAttributeFloat(element, "deteriorateonactive", 0.0f);
            //deteriorateOnUse = ToolBox.GetAttributeFloat(element, "deteriorateonuse", 0.0f);
        }


        //public virtual void Apply(ActionType type, float deltaTime, Item item, Character character = null)
        //{
        //    if (this.type == type) Apply(deltaTime, character, item);
        //}

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, IPropertyObject target)
        {
            if (targetNames != null && !targetNames.Contains(target.Name)) return;

            List<IPropertyObject> targets = new List<IPropertyObject>();
            targets.Add(target);

            if (this.type == type) Apply(deltaTime, entity, targets);
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, List<IPropertyObject> targets)
        {
            if (this.type == type) Apply(deltaTime, entity, targets);
        }

        protected virtual void Apply(float deltaTime, Entity entity, List<IPropertyObject> targets)
        {
            if (explosion != null) explosion.Explode(entity.SimPosition);

            if (sound != null) sound.Play(1.0f, 1000.0f, ConvertUnits.ToDisplayUnits(entity.SimPosition));

            for (int i = 0; i < propertyNames.Count(); i++)
            {
                ObjectProperty property;
                foreach (IPropertyObject target in targets)
                {
                    //if (targetNames!=null && !targetNames.Contains(target.Name)) continue;

                    if (!target.ObjectProperties.TryGetValue(propertyNames[i], out property)) continue;
                    
                    ApplyToProperty(property, propertyEffects[i], deltaTime);                    
                }
            }
        }

        //protected virtual void Apply(float deltaTime, Character character, Item item)
        //{
        //    if (explosion != null) explosion.Explode(item.SimPosition);

        //    if (sound != null) sound.Play(1.0f, 1000.0f, item.body.FarseerBody);
            
        //    for (int i = 0; i < propertyNames.Count(); i++)
        //    {
        //        ObjectProperty property;

        //        if (character!=null && character.properties.TryGetValue(propertyNames[i], out property))
        //        {
        //            ApplyToProperty(property, propertyEffects[i], deltaTime);                 
        //        }

        //        if (item == null) continue;

        //        if (item.properties.TryGetValue(propertyNames[i], out property))
        //        {
        //            ApplyToProperty(property, propertyEffects[i], deltaTime); 
        //        }

        //        foreach (ItemComponent ic in item.components)
        //        {
        //            if (!ic.properties.TryGetValue(propertyNames[i], out property)) continue;
        //            ApplyToProperty(property, propertyEffects[i], deltaTime); 
        //        }
        //    }
        //}

        protected void ApplyToProperty(ObjectProperty property, object value, float deltaTime)
        {
            if (disableDeltaTime) deltaTime = 1.0f;

            Type type = value.GetType();
            if (type == typeof(float))
            {
                float floatValue = (float)value * deltaTime;
                
                if (!setValue) floatValue += (float)property.GetValue();
                property.TrySetValue(floatValue);
            }
            else if (type == typeof(int))
            {
                int intValue = (int)((int)value * deltaTime);
                if (!setValue) intValue += (int)property.GetValue();
                property.TrySetValue(intValue);
            }
            else if (type == typeof(bool))
            {
                property.TrySetValue((bool)value);
            }
            else if (type == typeof(string))
            {
                property.TrySetValue((string)value);
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
