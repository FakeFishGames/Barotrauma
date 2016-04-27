using System.Xml.Linq;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    public class GUIStyle
    {
        private Dictionary<string, GUIComponentStyle> componentStyles;
                
        public GUIStyle(string file)
        {

            componentStyles = new Dictionary<string, GUIComponentStyle>();

            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading style ''" + file + "'' failed", e);
                return;
            }

            foreach (XElement subElement in doc.Root.Elements())
            {
                GUIComponentStyle componentStyle = new GUIComponentStyle(subElement);
                componentStyles.Add(subElement.Name.ToString().ToLowerInvariant(), componentStyle);
            }
        }

        public void Apply(GUIComponent targetComponent, GUIComponent parent = null)
        {
            GUIComponentStyle componentStyle = null;
            string name = (parent == null) ? targetComponent.GetType().Name.ToLowerInvariant() : parent.GetType().Name.ToLowerInvariant();
            componentStyles.TryGetValue(name, out componentStyle);

            if (componentStyle==null)
            {
                DebugConsole.ThrowError("Couldn't find a GUI style for "+targetComponent.GetType().Name);
                return;
            }

            targetComponent.ApplyStyle(componentStyle);            
        }


    }
}
