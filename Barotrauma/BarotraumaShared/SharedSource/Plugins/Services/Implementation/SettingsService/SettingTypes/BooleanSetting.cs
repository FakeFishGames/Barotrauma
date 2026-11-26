#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public class BooleanSetting : BaseSetting<bool>
    {
        public LocalizedString Label { get; set; }
        public LocalizedString? ToolTip { get; set; }

    #if CLIENT
        protected GUITickBox? TickBox;
    #endif

        public BooleanSetting(Identifier identifier, bool defaultValue, LocalizedString label) : base(identifier, defaultValue)
        {
            Label = label;
        }

        public override void Load(XElement element)
        {
            Value = element.GetAttributeBool("value", DefaultValue);
        }

        public override void Save(XElement element)
        {
            element.SetAttributeValue("value", Value);
        }

        public override void WriteMessage(IWriteMessage writeMessage)
        {
            writeMessage.WriteBoolean(Value);
        }

        public override void ReadMessage(IReadMessage readMessage)
        {
            Value = readMessage.ReadBoolean();
        }

    #if CLIENT
        public override void CreateUI()
        {
            MainUI = new GUIFrame(new RectTransform((1.0f, 0.07f), null), style: null);

            TickBox = new GUITickBox(new RectTransform(Vector2.One, MainUI.RectTransform), Label)
            {
                Selected = PendingValue,
                OnSelected = tickbox =>
                {
                    PendingValue = tickbox.Selected;
                    return true;
                }
            };

            TickBox.OnAddedToGUIUpdateList = tickBox => 
            {
                TickBox.Selected = PendingValue;
                TickBox.Enabled = CanClientEdit;
            };

            if (ToolTip != null)
            {
                TickBox.ToolTip = ToolTip;
            }
        }
    #endif
 
    }
}