using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;

namespace Subsurface
{
    class StatusEffect
    {

        [Flags]
        public enum Target 
        {
            This = 1, Parent = 2, Character = 4, Contained = 8, Nearby = 16
        }

        private Target targets;
        private string[] targetNames;

        public string[] propertyNames;
        private object[] propertyEffects;
        
        private string[] onContainingNames;

        public readonly ActionType type;

        private Explosion explosion;

        private Sound sound;
        
        public Target Targets
        {
            get { return targets; }
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
                            targets |= (Target)Enum.Parse(typeof(Target), s, true);
                        }

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


        public virtual void Apply(ActionType type, float deltaTime, Item item, Character character = null, Limb limb = null)
        {
            if (this.type == type) Apply(deltaTime, character, item);

        }

        
        protected virtual void Apply(float deltaTime, Character character, Item item, Limb limb = null)
        {
            if (explosion != null) explosion.Explode(item.SimPosition);

            if (sound != null) sound.Play(1.0f, 1000.0f, item.body.FarseerBody);
            
            for (int i = 0; i < propertyNames.Count(); i++)
            {
                ObjectProperty property;

                if (character!=null && character.properties.TryGetValue(propertyNames[i], out property))
                {
                    ApplyToProperty(property, propertyEffects[i], deltaTime);                 
                }

                if (item == null) continue;

                if (item.properties.TryGetValue(propertyNames[i], out property))
                {
                    ApplyToProperty(property, propertyEffects[i], deltaTime); 
                }

                foreach (ItemComponent ic in item.components)
                {
                    if (!ic.properties.TryGetValue(propertyNames[i], out property)) continue;
                    ApplyToProperty(property, propertyEffects[i], deltaTime); 
                }
            }
        }

        protected void ApplyToProperty(ObjectProperty property, object value, float deltaTime)
        {

            Type type = value.GetType();
            if (type == typeof(float))
            {
                property.TrySetValue((float)property.GetValue() + (float)value * deltaTime);
            }
            if (type == typeof(int))
            {
                property.TrySetValue((int)property.GetValue() + (int)value * deltaTime);
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
            for (int i = DelayedEffect.list.Count-1; i>= 0; i--)
            {
                DelayedEffect.list[i].Update(deltaTime);
            }
        }

    }
}
