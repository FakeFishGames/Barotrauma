using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class CustomInterface
    {
        partial void InitProjSpecific(XElement element)
        {
            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), GuiFrame.RectTransform, Anchor.Center))
            { RelativeSpacing = 0.1f, Stretch = true };

            foreach (CustomInterfaceElement ciElement in customInterfaceElementList)
            {
                if (ciElement.ContinuousSignal)
                {
                    var btn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), ciElement.text)
                    {
                        UserData = ciElement
                    };
                    btn.OnClicked += (_, userdata) =>
                    {
                        ButtonClicked(userdata as CustomInterfaceElement);
                        return true;
                    };
                }
                else
                {
                    var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), ciElement.text)
                    {
                        UserData = ciElement
                    };
                    tickBox.OnSelected += (tBox) =>
                    {
                        TickBoxToggled(tBox.UserData as CustomInterfaceElement, tBox.Selected);
                        return true;
                    };
                }
            }
        }        
    }
}
