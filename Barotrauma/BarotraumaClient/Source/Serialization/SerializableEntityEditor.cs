using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    class SerializableEntityEditor : GUIComponent
    {
        private GUIComponent editingHUD;

        public SerializableEntityEditor(ISerializableEntity entity, bool inGame, GUIComponent parent) : base("")
        {
            List<SerializableProperty> editableProperties = inGame ? 
                SerializableProperty.GetProperties<InGameEditable>(entity) : 
                SerializableProperty.GetProperties<Editable>(entity);

            /*int requiredItemCount = 0;
            if (!inGame)
            {
                foreach (ItemComponent ic in components)
                {
                    requiredItemCount += ic.requiredItems.Count;
                }
            }
            
            foreach (var objectProperty in editableProperties)
            {
                var editable = objectProperty.Attributes.OfType<Editable>().FirstOrDefault();
                if (editable != null) height += (int)(Math.Ceiling(editable.MaxLength / 40.0f) * 18.0f) + 5;
            }*/

            editingHUD = new GUIFrame(new Rectangle(0, 0, 0, 0), null, parent);
            editingHUD.Padding = new Vector4(10, 10, 00, 20);
            editingHUD.UserData = this;

            new GUITextBlock(new Rectangle(0, 0, 100, 20), entity.Name, "",
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.Font);

            int y = 20, padding = 5;
            foreach (var property in editableProperties)
            {
                //int boxHeight = 18;
                //var editable = property.Attributes.OfType<Editable>().FirstOrDefault();
                //if (editable != null) boxHeight = (int)(Math.Ceiling(editable.MaxLength / 40.0f) * 18.0f);

                object value = property.GetValue();

                GUIComponent propertyField = null;
                if (value is bool)
                {
                    propertyField = CreateBoolField(entity, property, (bool)value, y, editingHUD);
                }
                else if (value.GetType().IsEnum)
                {
                    propertyField = CreateEnumField(entity, property, value, y, editingHUD);
                }
                else if (value is string)
                {
                    propertyField = CreateStringField(entity, property, (string)value, y, editingHUD);
                }
                else if (value is int)
                {
                    propertyField = CreateIntField(entity, property, (int)value, y, editingHUD);
                }
                else if (value is float)
                {
                    propertyField = CreateFloatField(entity, property, (float)value, y, editingHUD);
                }

                if (propertyField != null)
                {
                    y += propertyField.Rect.Height + padding;
                }
            }

            editingHUD.SetDimensions(new Point(editingHUD.Rect.Width, editingHUD.children.Sum(c => c.Rect.Height) + 10), false);
        }

        private GUIComponent CreateBoolField(ISerializableEntity entity, SerializableProperty property, bool value, int yPos, GUIComponent parent)
        {
            GUITickBox propertyTickBox = new GUITickBox(new Rectangle(10, yPos, 18, 18), property.Name, Alignment.Left, parent);
            propertyTickBox.Font = GUI.SmallFont;
            propertyTickBox.Selected = value;

            propertyTickBox.OnSelected = (tickBox) =>
            {
                if (property.TrySetValue(tickBox.Selected))
                {
                    TrySendNetworkUpdate(entity, property);
                }
                return true;
            };

            return propertyTickBox;
        }

        private GUIComponent CreateIntField(ISerializableEntity entity, SerializableProperty property, int value, int yPos, GUIComponent parent)
        {
            new GUITextBlock(new Rectangle(10, yPos, 100, 18), property.Name, "", parent, GUI.SmallFont);
            GUINumberInput numberInput = new GUINumberInput(new Rectangle(180, yPos, 0, 18), "", Alignment.Left, null, null, parent);
            numberInput.Font = GUI.SmallFont;

            var editableAttribute = property.GetAttribute<Editable>();
            numberInput.MinValue = editableAttribute.MinValueInt;
            numberInput.MinValue = editableAttribute.MaxValueInt;

            numberInput.OnValueChanged += (numInput, number) =>
            {
                if (property.TrySetValue(number))
                {
                    TrySendNetworkUpdate(entity, property);
                }
            };

            return numberInput;
        }

        private GUIComponent CreateFloatField(ISerializableEntity entity, SerializableProperty property, float value, int yPos, GUIComponent parent)
        {
            new GUITextBlock(new Rectangle(10, yPos, 100, 18), property.Name, "", parent, GUI.SmallFont);
            GUINumberInput numberInput = new GUINumberInput(new Rectangle(180, yPos, 0, 18), "", Alignment.Left, null, null, parent);
            numberInput.Font = GUI.SmallFont;

            return numberInput;
        }

        private GUIComponent CreateEnumField(ISerializableEntity entity, SerializableProperty property, object value, int yPos, GUIComponent parent)
        {
            new GUITextBlock(new Rectangle(0, yPos, 100, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, editingHUD, false, GUI.SmallFont);
            GUIDropDown enumDropDown = new GUIDropDown(new Rectangle(180, yPos, 0, 18), "", "", Alignment.TopLeft, parent);
            foreach (object enumValue in Enum.GetValues(value.GetType()))
            {
                var enumTextBlock = new GUITextBlock(new Rectangle(0, 0, 200, 25), enumValue.ToString(), "", enumDropDown);
                enumTextBlock.UserData = enumValue;
            }

            enumDropDown.OnSelected += (selected, val) =>
            {
                if (property.TrySetValue(val))
                {
                    TrySendNetworkUpdate(entity, property);
                }
                return true;
            };

            enumDropDown.SelectItem(value);

            return enumDropDown;
        }

        private GUIComponent CreateStringField(ISerializableEntity entity, SerializableProperty property, string value, int yPos, GUIComponent parent)
        {
            new GUITextBlock(new Rectangle(0, yPos, 100, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            GUITextBox propertyBox = new GUITextBox(new Rectangle(0, yPos, 250, 18), Alignment.Right, "", parent);
            propertyBox.Font = GUI.SmallFont;
            //if (boxHeight > 18) propertyBox.Wrap = true;

            propertyBox.Text = value;            
            propertyBox.OnEnterPressed = (textBox, text) =>
            {
                if (property.TrySetValue(value))
                {
                    TrySendNetworkUpdate(entity, property);
                }
                return true;
            };
            
            return propertyBox;
        }
        
        private void TrySendNetworkUpdate(ISerializableEntity entity, SerializableProperty property)
        {
            if (GameMain.Server != null)
            {
                IServerSerializable serverSerializable = entity as IServerSerializable;
                if (serverSerializable != null)
                {
                    GameMain.Server.CreateEntityEvent(serverSerializable, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
            }
            else if (GameMain.Client != null)
            {
                IClientSerializable clientSerializable = entity as IClientSerializable;
                if (clientSerializable != null)
                {
                    GameMain.Client.CreateEntityEvent(clientSerializable, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
            }
        }        
    }

}
