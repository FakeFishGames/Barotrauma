using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class SerializableEntityEditor : GUIComponent
    {
        private static readonly string[] vectorComponentLabels = { "X", "Y", "Z", "W" };
        private static readonly string[] rectComponentLabels = { "X", "Y", "W", "H" };
        private static readonly string[] colorComponentLabels = { "R", "G", "B", "A" };

        public float ElementHeight { get; set; } = 0.035f;
        private GUILayoutGroup layoutGroup;

        /// <summary>
        /// This is the new editor.
        /// </summary>
        public SerializableEntityEditor(RectTransform parent, ISerializableEntity entity, bool inGame, bool showName, string style = "") : base(style, new RectTransform(new Vector2(0.9f, 0.9f), parent, Anchor.Center))
        {
            List<SerializableProperty> editableProperties = inGame ? 
                SerializableProperty.GetProperties<InGameEditable>(entity) : 
                SerializableProperty.GetProperties<Editable>(entity);

            if (showName)
            {
                new GUITextBlock(new RectTransform(new Vector2(1, 0.1f), RectTransform), entity.Name, font: GUI.Font);
                layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.9f), RectTransform, Anchor.BottomCenter));
            }
            else
            {
                layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, RectTransform));
            }
            editableProperties.ForEach(ep => CreateNewField(ep, entity));
        }

        // TODO: remove or refactor? The new system uses a list box component.
        public void AddCustomContent(GUIComponent component, int childIndex)
        {
            childIndex = MathHelper.Clamp(childIndex, 0, Children.Count);

            AddChild(component);
            Children.Remove(component);
            Children.Insert(childIndex, component);

            if (childIndex > 0 )
            {
                component.Rect = new Rectangle(component.Rect.X, Children[childIndex - 1].Rect.Bottom, component.Rect.Width, component.Rect.Height);
            }

            for (int i = childIndex + 1; i < Children.Count; i++)
            {
                Children[i].Rect = new Rectangle(Children[i].Rect.X, Children[i].Rect.Y + component.Rect.Height, Children[i].Rect.Width, Children[i].Rect.Height);
            }
            SetDimensions(new Point(Rect.Width, Children.Last().Rect.Bottom - Rect.Y + 10), false);
        }

        private GUIComponent CreateNewField(SerializableProperty property, ISerializableEntity entity)
        {
            object value = property.GetValue();
            if (property.PropertyType == typeof(string) && value == null)
            {
                value = "";
            }
            GUIComponent propertyField = null;
            if (value is bool)
            {
                propertyField = CreateBoolField(entity, property, (bool)value);
            }
            else if (value is string)
            {
                propertyField = CreateStringField(entity, property, (string)value);
            }
            else if (value.GetType().IsEnum)
            {
                propertyField = CreateEnumField(entity, property, value);
            }
            else if (value is int)
            {
                propertyField = CreateIntField(entity, property, (int)value);
            }
            else if (value is float)
            {
                propertyField = CreateFloatField(entity, property, (float)value);
            }
            else if (value is Vector2)
            {
                propertyField = CreateVector2Field(entity, property, (Vector2)value);
            }
            else if (value is Vector3)
            {
                propertyField = CreateVector3Field(entity, property, (Vector3)value);
            }
            else if (value is Vector4)
            {
                propertyField = CreateVector4Field(entity, property, (Vector4)value);
            }
            else if (value is Color)
            {
                propertyField = CreateColorField(entity, property, (Color)value);
            }
            else if (value is Rectangle)
            {
                propertyField = CreateRectangleField(entity, property, (Rectangle)value);
            }
            return propertyField;
        }

        private GUIComponent CreateBoolField(ISerializableEntity entity, SerializableProperty property, bool value)
        {
            GUITickBox propertyTickBox = new GUITickBox(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), property.Name)
            {
                Font = GUI.SmallFont,
                Selected = value,
                ToolTip = property.GetAttribute<Editable>().ToolTip,
                OnSelected = (tickBox) =>
                {
                    if (property.TrySetValue(tickBox.Selected))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                    return true;
                }
            };
            return propertyTickBox;
        }

        private GUIComponent CreateIntField(ISerializableEntity entity, SerializableProperty property, int value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform,
                Anchor.TopRight), GUINumberInput.NumberType.Int)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip,
                Font = GUI.SmallFont
            };
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
            return frame;
        }

        private GUIComponent CreateFloatField(ISerializableEntity entity, SerializableProperty property, float value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform,
                Anchor.TopRight), GUINumberInput.NumberType.Float)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip,
                Font = GUI.SmallFont
            };
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
            return frame;
        }

        private GUIComponent CreateEnumField(ISerializableEntity entity, SerializableProperty property, object value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUIDropDown enumDropDown = new GUIDropDown(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform, Anchor.TopRight))
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            foreach (object enumValue in Enum.GetValues(value.GetType()))
            {
                enumDropDown.AddItem(enumValue.ToString(), enumValue);
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
            return frame;
        }

        private GUIComponent CreateStringField(ISerializableEntity entity, SerializableProperty property, string value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUITextBox propertyBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform, Anchor.TopRight))
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip,
                Font = GUI.SmallFont,
                Text = value,
                OnEnterPressed = (textBox, text) =>
                {
                    if (property.TrySetValue(text))
                    {
                        TrySendNetworkUpdate(entity, property);
                        textBox.Text = (string)property.GetValue();
                        textBox.Deselect();
                    }
                    return true;
                }
            };
            return frame;
        }

        private GUIComponent CreateVector2Field(ISerializableEntity entity, SerializableProperty property, Vector2 value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            //TODO
            return frame;
        }

        private GUIComponent CreateVector3Field(ISerializableEntity entity, SerializableProperty property, Vector3 value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            //TODO
            return frame;
        }

        private GUIComponent CreateVector4Field(ISerializableEntity entity, SerializableProperty property, Vector4 value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            //TODO
            return frame;
        }

        private GUIComponent CreateColorField(ISerializableEntity entity, SerializableProperty property, Color value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            //TODO
            return frame;
        }

        private GUIComponent CreateRectangleField(ISerializableEntity entity, SerializableProperty property, Rectangle value)
        {
            var frame = new GUIFrame(new RectTransform(new Vector2(1, ElementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            //TODO
            return frame;
        }

        #region obsolete
        [Obsolete("Use RectTransform")]
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
                if (property.PropertyType == typeof(string) && value == null)
                {
                    value = "";
                }

                GUIComponent propertyField = null;
                if (value is bool)
                {
                    propertyField = CreateBoolField(entity, property, (bool)value, y, this);
                }
                else if (value is string)
                {
                    propertyField = CreateStringField(entity, property, (string)value, y, this);
                }
                else if (value.GetType().IsEnum)
                {
                    propertyField = CreateEnumField(entity, property, value, y, this);
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

            if (Children.Count > 0)
            {
                SetDimensions(new Point(Rect.Width, Children.Last().Rect.Bottom - Rect.Y + 10), false);
            }
            else
            {
                SetDimensions(new Point(Rect.Width, 0), false);
            }

            if (parent is GUIListBox)
            {
                ((GUIListBox)parent).UpdateScrollBarSize();
            }
        }

        [Obsolete]
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

        [Obsolete]
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

        [Obsolete]
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

        [Obsolete]
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

        [Obsolete]
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
                if (property.TrySetValue(text))
                {
                    TrySendNetworkUpdate(entity, property);
                    textBox.Text = (string)property.GetValue();
                    textBox.Deselect();
                }
                return true;
            };
            
            return propertyBox;
        }

        [Obsolete]
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

        [Obsolete]
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

        [Obsolete]
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

        [Obsolete]
        private GUIComponent CreateColorField(ISerializableEntity entity, SerializableProperty property, Color value, int yPos, GUIComponent parent)
        {
            var label = new GUITextBlock(new Rectangle(0, yPos, 0, 18), property.Name, "", Alignment.TopLeft, Alignment.Left, parent, false, GUI.SmallFont);
            label.ToolTip = property.GetAttribute<Editable>().ToolTip;

            var colorBoxBack = new GUIFrame(new Rectangle(110 - 1, yPos - 1, 25 + 2, 18 + 2), Color.Black, Alignment.TopLeft, null, parent);
            var colorBox = new GUIFrame(new Rectangle(110, yPos, 25, 18), value, Alignment.TopLeft, null, parent);

            for (int i = 0; i < 4; i++)
            {
                new GUITextBlock(new Rectangle(140 + i * 70, yPos, 100, 18), colorComponentLabels[i], "", Alignment.TopLeft, Alignment.CenterLeft, parent, false, GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new Rectangle(160 + i * 70, yPos, 45, 18), "", GUINumberInput.NumberType.Int, Alignment.Left, parent);
                numberInput.MinValueInt = 0;
                numberInput.MaxValueInt = 255;

                if (i == 0)
                    numberInput.IntValue = value.R;
                else if (i == 1)
                    numberInput.IntValue = value.G;
                else if (i == 2)
                    numberInput.IntValue = value.B;
                else
                    numberInput.IntValue = value.A;

                numberInput.Font = GUI.SmallFont;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Color newVal = (Color)property.GetValue();
                    if (comp == 0)
                        newVal.R = (byte)(numInput.IntValue);
                    else if (comp == 1)
                        newVal.G = (byte)(numInput.IntValue);
                    else if (comp == 2)
                        newVal.B = (byte)(numInput.IntValue);
                    else
                        newVal.A = (byte)(numInput.IntValue);

                    if (property.TrySetValue(newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                        colorBox.Color = newVal;
                    }
                };
            }

            return label;
        }

        [Obsolete]
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
        #endregion

        private void TrySendNetworkUpdate(ISerializableEntity entity, SerializableProperty property)
        {
            if (entity is ItemComponent e)
            {
                entity = e.Item;
            }

            if (GameMain.Server != null)
            {
                if (entity is IServerSerializable serverSerializable)
                {
                    GameMain.Server.CreateEntityEvent(serverSerializable, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
            }
            else if (GameMain.Client != null)
            {
                if (entity is IClientSerializable clientSerializable)
                {
                    GameMain.Client.CreateEntityEvent(clientSerializable, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
            }
        }
    }

}
