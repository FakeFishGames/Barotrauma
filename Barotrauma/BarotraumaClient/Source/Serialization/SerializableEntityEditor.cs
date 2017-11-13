using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class SerializableEntityEditor : GUIComponent
    {

        private static readonly string[] vectorComponentLabels = { "X", "Y", "Z", "W" };
        private static readonly string[] rectComponentLabels = { "X", "Y", "W", "H" };
        private static readonly string[] colorComponentLabels = { "R", "G", "B", "A" };

        //private GUIComponent editingHUD;

        public SerializableEntityEditor(ISerializableEntity entity, bool inGame, GUIComponent parent, bool showName) : base("")
        {
            List<SerializableProperty> editableProperties = inGame ? 
                SerializableProperty.GetProperties<InGameEditable>(entity) : 
                SerializableProperty.GetProperties<Editable>(entity);
            
            if (parent != null) parent.AddChild(this);
            
            if (showName)
            {
                new GUITextBlock(new Rectangle(0, 0, 100, 20), entity.Name, "",
                    Alignment.TopLeft, Alignment.TopLeft, this, false, GUI.Font);
            }

            int y = showName ? 30 : 10, padding = 10;
            foreach (var property in editableProperties)
            {
                //int boxHeight = 18;
                //var editable = property.Attributes.OfType<Editable>().FirstOrDefault();
                //if (editable != null) boxHeight = (int)(Math.Ceiling(editable.MaxLength / 40.0f) * 18.0f);

                object value = property.GetValue();

                GUIComponent propertyField = null;
                if (value is bool)
                {
                    propertyField = CreateBoolField(entity, property, (bool)value, y, this);
                }
                else if (value.GetType().IsEnum)
                {
                    propertyField = CreateEnumField(entity, property, value, y, this);
                }
                else if (value is string)
                {
                    propertyField = CreateStringField(entity, property, (string)value, y, this);
                }
                else if (value is int)
                {
                    propertyField = CreateIntField(entity, property, (int)value, y, this);
                }
                else if (value is float)
                {
                    propertyField = CreateFloatField(entity, property, (float)value, y, this);
                }
                else if (value is Vector2)
                {
                    propertyField = CreateVector2Field(entity, property, (Vector2)value, y, this);
                }
                else if (value is Vector3)
                {
                    propertyField = CreateVector3Field(entity, property, (Vector3)value, y, this);
                }
                else if (value is Vector4)
                {
                    propertyField = CreateVector4Field(entity, property, (Vector4)value, y, this);
                }
                else if (value is Color)
                {
                    propertyField = CreateColorField(entity, property, (Color)value, y, this);
                }
                else if (value is Rectangle)
                {
                    propertyField = CreateRectangleField(entity, property, (Rectangle)value, y, this);
                }

                if (propertyField != null)
                {
                    y += propertyField.Rect.Height + padding;
                }
            }

            SetDimensions(new Point(Rect.Width, children.Last().Rect.Bottom - Rect.Y + 10), false);
        }

        public void AddCustomContent(GUIComponent component, int childIndex)
        {
            childIndex = MathHelper.Clamp(childIndex, 0, children.Count);

            AddChild(component);
            children.Remove(component);
            children.Insert(childIndex, component);

            if (childIndex > 0 )
            {
                component.Rect = new Rectangle(component.Rect.X, children[childIndex - 1].Rect.Bottom, component.Rect.Width, component.Rect.Height);
            }

            for (int i = childIndex + 1; i < children.Count; i++)
            {
                children[i].Rect = new Rectangle(children[i].Rect.X, children[i].Rect.Y + component.Rect.Height, children[i].Rect.Width, children[i].Rect.Height);
            }
            SetDimensions(new Point(Rect.Width, children.Last().Rect.Bottom - Rect.Y + 10), false);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            DrawChildren(spriteBatch);
        }

        private GUIComponent CreateBoolField(ISerializableEntity entity, SerializableProperty property, bool value, int yPos, GUIComponent parent)
        {
            GUITickBox propertyTickBox = new GUITickBox(new Rectangle(10, yPos, 18, 18), property.Name, Alignment.Left, parent);
            propertyTickBox.Font = GUI.SmallFont;
            propertyTickBox.Selected = value;
            propertyTickBox.ToolTip = property.GetAttribute<Editable>().ToolTip;

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
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            GUINumberInput numberInput = new GUINumberInput(new Rectangle(180, yPos, 0, 18), "", GUINumberInput.NumberType.Int, Alignment.Left, parent);
            numberInput.ToolTip = property.GetAttribute<Editable>().ToolTip;
            numberInput.Font = GUI.SmallFont;

            var editableAttribute = property.GetAttribute<Editable>();
            numberInput.MinValueInt = editableAttribute.MinValueInt;
            numberInput.MaxValueInt = editableAttribute.MaxValueInt;

            numberInput.IntValue = value;

            numberInput.OnValueChanged += (numInput) =>
            {
                if (property.TrySetValue(numInput.IntValue))
                {
                    TrySendNetworkUpdate(entity, property);
                }
            };

            return numberInput;
        }

        private GUIComponent CreateFloatField(ISerializableEntity entity, SerializableProperty property, float value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            GUINumberInput numberInput = new GUINumberInput(new Rectangle(180, yPos, 0, 18), "", GUINumberInput.NumberType.Float, Alignment.Left, parent);
            numberInput.ToolTip = property.GetAttribute<Editable>().ToolTip;
            numberInput.Font = GUI.SmallFont;

            var editableAttribute = property.GetAttribute<Editable>();
            numberInput.MinValueFloat = editableAttribute.MinValueFloat;
            numberInput.MaxValueFloat = editableAttribute.MaxValueFloat;

            numberInput.FloatValue = value;

            numberInput.OnValueChanged += (numInput) =>
            {
                if (property.TrySetValue(numInput.FloatValue))
                {
                    TrySendNetworkUpdate(entity, property);
                }
            };

            return numberInput;
        }

        private GUIComponent CreateEnumField(ISerializableEntity entity, SerializableProperty property, object value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            GUIDropDown enumDropDown = new GUIDropDown(new Rectangle(180, yPos, 0, 18), "", "", Alignment.TopLeft, parent);
            enumDropDown.ToolTip = property.GetAttribute<Editable>().ToolTip;

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
            int boxHeight = 18;
            var editable = property.GetAttribute<Editable>();
            boxHeight = (int)(Math.Ceiling(editable.MaxLength / 40.0f) * boxHeight);

            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            GUITextBox propertyBox = new GUITextBox(new Rectangle(0, yPos, 250, boxHeight), Alignment.Right, "", parent);
            propertyBox.ToolTip = editable.ToolTip;
            propertyBox.Font = GUI.SmallFont;

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
        
        private GUIComponent CreateVector2Field(ISerializableEntity entity, SerializableProperty property, Vector2 value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;

            for (int i = 0; i < 2; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), vectorComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Float, Alignment.Left, parent);
                numberInput.Font = GUI.SmallFont;

                if (i == 0)
                    numberInput.FloatValue = value.X;
                else
                    numberInput.FloatValue = value.Y;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Vector2 newVal = (Vector2)property.GetValue();
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else
                        newVal.Y = numInput.FloatValue;

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
            }

            return label;
        }

        private GUIComponent CreateVector3Field(ISerializableEntity entity, SerializableProperty property, Vector3 value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            for (int i = 0; i < 3; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), vectorComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Float, Alignment.Left, parent);
                numberInput.Font = GUI.SmallFont;

                if (i == 0)
                    numberInput.FloatValue = value.X;
                else if (i == 1)
                    numberInput.FloatValue = value.Y;
                else if (i == 2)
                    numberInput.FloatValue = value.Z;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Vector3 newVal = (Vector3)property.GetValue();
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else if (comp == 1)
                        newVal.Y = numInput.FloatValue;
                    else
                        newVal.Z = numInput.FloatValue;

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
            }

            return label;
        }

        private GUIComponent CreateVector4Field(ISerializableEntity entity, SerializableProperty property, Vector4 value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            for (int i = 0; i < 4; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), vectorComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Float, Alignment.Left, parent);
                numberInput.Font = GUI.SmallFont;

                if (i == 0)
                    numberInput.FloatValue = value.X;
                else if (i == 1)
                    numberInput.FloatValue = value.Y;
                else if (i == 2)
                    numberInput.FloatValue = value.Z;
                else
                    numberInput.FloatValue = value.W;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Vector4 newVal = (Vector4)property.GetValue();
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else if (comp == 1)
                        newVal.Y = numInput.FloatValue;
                    else if (comp == 2)
                        newVal.Z = numInput.FloatValue;
                    else
                        newVal.W = numInput.FloatValue;

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
            }

            return label;
        }

        private GUIComponent CreateColorField(ISerializableEntity entity, SerializableProperty property, Color value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;

            var colorBoxBack = new GUIFrame(new Rectangle(110 - 1, yPos - 1, 25 + 2, 18 + 2), Color.Black, Alignment.TopLeft, null, parent);
            var colorBox = new GUIFrame(new Rectangle(110, yPos , 25, 18), value, Alignment.TopLeft, null, parent);

            for (int i = 0; i < 4; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), colorComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Float, Alignment.Left, parent);
                numberInput.MinValueFloat = 0.0f;
                numberInput.MaxValueFloat = 1.0f;

                if (i == 0)
                    numberInput.FloatValue = value.R / 255.0f;
                else if (i == 1)
                    numberInput.FloatValue = value.G / 255.0f;
                else if (i == 2)
                    numberInput.FloatValue = value.B / 255.0f;
                else
                    numberInput.FloatValue = value.A / 255.0f;

                numberInput.Font = GUI.SmallFont;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Color newVal = (Color)property.GetValue();
                    if (comp == 0)
                        newVal.R = (byte)(numInput.FloatValue * 255);
                    else if (comp == 1)
                        newVal.G = (byte)(numInput.FloatValue * 255);
                    else if (comp == 2)
                        newVal.B = (byte)(numInput.FloatValue * 255);
                    else
                        newVal.A = (byte)(numInput.FloatValue * 255);

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                        colorBox.Color = newVal;
                    }
                };
            }

            return label;
        }

        private GUIComponent CreateRectangleField(ISerializableEntity entity, SerializableProperty property, Rectangle value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;
            for (int i = 0; i < 4; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), rectComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Int, Alignment.Left, parent);
                numberInput.Font = GUI.SmallFont;

                if (i == 0)
                    numberInput.IntValue = value.X;
                else if (i == 1)
                    numberInput.IntValue = value.Y;
                else if (i == 2)
                    numberInput.IntValue = value.Width;
                else
                    numberInput.IntValue = value.Height;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Rectangle newVal = (Rectangle)property.GetValue();
                    if (comp == 0)
                        newVal.X = numInput.IntValue;
                    else if (comp == 1)
                        newVal.Y = numInput.IntValue;
                    else if (comp == 2)
                        newVal.Width = numInput.IntValue;
                    else
                        newVal.Height = numInput.IntValue;

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
            }

            return label;
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
