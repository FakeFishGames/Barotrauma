using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class SerializableEntityEditor : GUIComponent
    {
        private static readonly string[] vectorComponentLabels = { "X", "Y", "Z", "W" };
        private static readonly string[] rectComponentLabels = { "X", "Y", "W", "H" };
        private static readonly string[] colorComponentLabels = { "R", "G", "B", "A" };

        private int elementHeight;
        private GUILayoutGroup layoutGroup;

        public int ContentHeight
        {
            get
            {
                if (layoutGroup.NeedsToRecalculate) layoutGroup.Recalculate();

                int spacing = layoutGroup.CountChildren == 0 ? 0 : ((layoutGroup.CountChildren - 1) * layoutGroup.AbsoluteSpacing);
                return spacing + layoutGroup.Children.Sum(c => c.RectTransform.NonScaledSize.Y);
            }
        }

        /// <summary>
        /// Holds the references to the input fields.
        /// </summary>
        public Dictionary<SerializableProperty, GUIComponent[]> Fields { get; private set; } = new Dictionary<SerializableProperty, GUIComponent[]>();

        /// <summary>
        /// This is the new editor.
        /// </summary>
        public SerializableEntityEditor(RectTransform parent, ISerializableEntity entity, bool inGame, bool showName, string style = "", int elementHeight = 20) : base(style, new RectTransform(Vector2.One, parent))
        {
            this.elementHeight = elementHeight;
            List<SerializableProperty> editableProperties = inGame ? 
                SerializableProperty.GetProperties<InGameEditable>(entity) : 
                SerializableProperty.GetProperties<Editable>(entity);
            
            layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, RectTransform)) { AbsoluteSpacing = 2 };
            if (showName)
            {
                new GUITextBlock(new RectTransform(new Point(layoutGroup.Rect.Width, elementHeight), layoutGroup.RectTransform), entity.Name, font: GUI.Font);
            }
            editableProperties.ForEach(ep => CreateNewField(ep, entity));

            //scale the size of this component and the layout group to fit the children
            int contentHeight = ContentHeight;
            RectTransform.NonScaledSize = new Point(RectTransform.NonScaledSize.X, contentHeight);
            layoutGroup.RectTransform.NonScaledSize = new Point(layoutGroup.RectTransform.NonScaledSize.X, contentHeight);
        }

        // TODO: remove or refactor? The new system uses a layout group.
        public void AddCustomContent(GUIComponent component, int childIndex)
        {
            component.RectTransform.Parent = layoutGroup.RectTransform;
            component.RectTransform.RepositionChildInHierarchy(childIndex);

            int contentHeight = ContentHeight;
            RectTransform.NonScaledSize = new Point(RectTransform.NonScaledSize.X, contentHeight);
            layoutGroup.RectTransform.NonScaledSize = new Point(layoutGroup.RectTransform.NonScaledSize.X, contentHeight);
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
            GUITickBox propertyTickBox = new GUITickBox(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), property.Name)
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
            Fields.Add(property, new GUIComponent[] { propertyTickBox });
            return propertyTickBox;
        }

        private GUIComponent CreateIntField(ISerializableEntity entity, SerializableProperty property, int value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform,
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
            Fields.Add(property, new GUIComponent[] { numberInput });
            return frame;
        }

        private GUIComponent CreateFloatField(ISerializableEntity entity, SerializableProperty property, float value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform,
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
            Fields.Add(property, new GUIComponent[] { numberInput });
            return frame;
        }

        private GUIComponent CreateEnumField(ISerializableEntity entity, SerializableProperty property, object value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUIDropDown enumDropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform, Anchor.TopRight))
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
            Fields.Add(property, new GUIComponent[] { enumDropDown });
            return frame;
        }

        private GUIComponent CreateStringField(ISerializableEntity entity, SerializableProperty property, string value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont, textAlignment: Alignment.Left)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            GUITextBox propertyBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform, Anchor.TopRight))
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
            Fields.Add(property, new GUIComponent[] { propertyBox });
            return frame;
        }

        private GUIComponent CreateVector2Field(ISerializableEntity entity, SerializableProperty property, Vector2 value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true)
            {
                Stretch = false,
                RelativeSpacing = 0.05f
            };
            var fields = new GUIComponent[2];
            for (int i = 0; i < 2; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), inputArea.RectTransform), color: Color.Transparent);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform), vectorComponentLabels[i], font: GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.TopRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

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
                fields[i] = numberInput;
            }
            Fields.Add(property, fields);
            return frame;
        }

        private GUIComponent CreateVector3Field(ISerializableEntity entity, SerializableProperty property, Vector3 value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true)
            {
                Stretch = false,
                RelativeSpacing = 0.03f
            };
            var fields = new GUIComponent[3];
            for (int i = 0; i < 3; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.3f, 1), inputArea.RectTransform), color: Color.Transparent);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform), vectorComponentLabels[i], font: GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.TopRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

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
                fields[i] = numberInput;
            }
            Fields.Add(property, fields);
            return frame;
        }

        private GUIComponent CreateVector4Field(ISerializableEntity entity, SerializableProperty property, Vector4 value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var fields = new GUIComponent[4];
            for (int i = 0; i < 4; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform), color: Color.Transparent, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), element.RectTransform), vectorComponentLabels[i], textAlignment: Alignment.Center, font: GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.8f, 1), element.RectTransform, Anchor.TopRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

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
                fields[i] = numberInput;
            }
            Fields.Add(property, fields);
            return frame;
        }

        private GUIComponent CreateColorField(ISerializableEntity entity, SerializableProperty property, Color value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            var colorBoxBack = new GUIFrame(new RectTransform(new Vector2(0.075f, 1), frame.RectTransform)
            {
                RelativeOffset = new Vector2(0.2f, 0)
            }, color: Color.Black, style: null);
            var colorBox = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), colorBoxBack.RectTransform, Anchor.Center), style: null);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            var fields = new GUIComponent[4];
            for (int i = 0; i < 4; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform), color: Color.Transparent, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.25f, 1), element.RectTransform), colorComponentLabels[i], textAlignment: Alignment.Center, font: GUI.SmallFont);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.TopRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };
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
                colorBox.Color = (Color)property.GetValue();
                fields[i] = numberInput;
            }
            Fields.Add(property, fields);
            return frame;
        }

        private GUIComponent CreateRectangleField(ISerializableEntity entity, SerializableProperty property, Rectangle value)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), frame.RectTransform), property.Name, font: GUI.SmallFont)
            {
                ToolTip = property.GetAttribute<Editable>().ToolTip
            };
            var fields = new GUIComponent[4];
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            for (int i = 0; i < 4; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.5f, 1), inputArea.RectTransform), color: Color.Transparent, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.25f, 1), element.RectTransform), rectComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.Center);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.TopRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };

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
                fields[i] = numberInput;
            }
            Fields.Add(property, fields);
            return frame;
        }
        
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
