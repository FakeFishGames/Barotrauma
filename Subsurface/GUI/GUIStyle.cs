using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System;

namespace Subsurface
{
    class GUIStyle
    {
        public readonly Vector4 smallPadding;
        public readonly Vector4 largePadding;

        public readonly Color backGroundColor;
        public readonly Color foreGroundColor;

        public readonly Color textColor;
        
        public readonly Color hoverColor;
        public readonly Color selectedColor;
                
        public GUIStyle(string file)
        {
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading style ''" + file + "'' failed", e);
                return;
            }

            smallPadding = ToolBox.GetAttributeVector4(doc.Root, "smallpadding", Vector4.Zero);
            largePadding = ToolBox.GetAttributeVector4(doc.Root, "largepadding", Vector4.Zero);

            Vector4 colorVector = ToolBox.GetAttributeVector4(doc.Root, "backgroundcolor", new Vector4(0.0f,0.0f,0.0f,1.0f));
            backGroundColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(doc.Root, "foregroundcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            foreGroundColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
            
            colorVector = ToolBox.GetAttributeVector4(doc.Root, "textcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            textColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(doc.Root, "hovercolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            hoverColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(doc.Root, "selectedcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            selectedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
        }

    }
}
