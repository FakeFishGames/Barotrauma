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
            try
            {
                ToolBox.IsProperFilenameCase(file);
                doc = XDocument.Load(file);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading style \"" + file + "\" failed", e);
                return;
            }

            foreach (XElement subElement in doc.Root.Elements())
            {
                GUIComponentStyle componentStyle = new GUIComponentStyle(subElement);
                componentStyles.Add(subElement.Name.ToString().ToLowerInvariant(), componentStyle);
            }
        }

        public void Apply(GUIComponent targetComponent, string styleName = "", GUIComponent parent = null)
        {
            if (string.IsNullOrEmpty(styleName))
            {
                styleName = parent == null ? targetComponent.GetType().Name.ToLowerInvariant() : parent.GetType().Name.ToLowerInvariant();
            }

            GUIComponentStyle componentStyle = null;            

            if (!componentStyles.TryGetValue(styleName, out componentStyle))
            {
                DebugConsole.ThrowError("Couldn't find a GUI style \""+ styleName+"\"");
                return;
            }

            targetComponent.ApplyStyle(componentStyle);            
        }


    }
}
