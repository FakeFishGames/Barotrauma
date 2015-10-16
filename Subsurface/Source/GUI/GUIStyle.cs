using System.Xml.Linq;
using Microsoft.Xna.Framework;
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

            //smallPadding = ToolBox.GetAttributeVector4(doc.Root, "smallpadding", Vector4.Zero);
            //largePadding = ToolBox.GetAttributeVector4(doc.Root, "largepadding", Vector4.Zero);

            //Vector4 colorVector = ToolBox.GetAttributeVector4(doc.Root, "backgroundcolor", new Vector4(0.0f,0.0f,0.0f,1.0f));
            //backGroundColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            //colorVector = ToolBox.GetAttributeVector4(doc.Root, "foregroundcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            //foreGroundColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
            
            //colorVector = ToolBox.GetAttributeVector4(doc.Root, "textcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            //textColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            //colorVector = ToolBox.GetAttributeVector4(doc.Root, "hovercolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            //hoverColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            //colorVector = ToolBox.GetAttributeVector4(doc.Root, "selectedcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            //selectedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            foreach (XElement subElement in doc.Root.Elements())
            {
                GUIComponentStyle componentStyle = new GUIComponentStyle(subElement);
                componentStyles.Add(subElement.Name.ToString().ToLower(), componentStyle);
            }
        }

        public void Apply(GUIComponent targetComponent, GUIComponent parent = null)
        {
            GUIComponentStyle componentStyle = null;
            string name = (parent==null) ? targetComponent.GetType().Name.ToLower() : parent.GetType().Name.ToLower();
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
