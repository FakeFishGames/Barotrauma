using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class SerializableEntityEditor : GUIComponent
    {
        private int elementHeight;
        private GUILayoutGroup layoutGroup;
        private float inputFieldWidth = 0.5f;
        private float largeInputFieldWidth = 0.8f;
#if DEBUG
        public static List<string> MissingLocalizations = new List<string>();
#endif

        public int ContentHeight
        {
            get
            {
                if (layoutGroup.NeedsToRecalculate) layoutGroup.Recalculate();

                int spacing = layoutGroup.CountChildren == 0 ? 0 : ((layoutGroup.CountChildren - 1) * layoutGroup.AbsoluteSpacing);
                return spacing + layoutGroup.Children.Sum(c => c.RectTransform.NonScaledSize.Y);
            }
        }

        public int ContentCount
        {
            get { return layoutGroup.CountChildren; }
        }

        /// <summary>
        /// Holds the references to the input fields.
        /// </summary>
        public Dictionary<string, GUIComponent[]> Fields { get; private set; } = new Dictionary<string, GUIComponent[]>();

        public void UpdateValue(SerializableProperty property, object newValue, bool flash = true)
        {
            if (!Fields.TryGetValue(property.Name, out GUIComponent[] fields))
            {
                DebugConsole.ThrowError($"No field for {property.Name} found!");
                return;
            }
            if (newValue is float f)
            {
                foreach (var field in fields)
                {
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Float)
                        {
                            numInput.FloatValue = f;
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is int integer)
            {
                foreach (var field in fields)
                {
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Int)
                        {
                            numInput.IntValue = integer;
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is bool b)
            {
                if (fields[0] is GUITickBox tickBox)
                {
                    tickBox.Selected = b;
                    if (flash)
                    {
                        tickBox.Flash(GUI.Style.Green);
                    }
                }
            }
            else if (newValue is string s)
            {
                if (fields[0] is GUITextBox textBox)
                {
                    textBox.Text = s;
                    if (flash)
                    {
                        textBox.Flash(GUI.Style.Green);
                    }
                }
            }
            else if (newValue.GetType().IsEnum)
            {
                if (fields[0] is GUIDropDown dropDown)
                {
                    dropDown.Select((int)newValue);
                    if (flash)
                    {
                        dropDown.Flash(GUI.Style.Green);
                    }
                }
            }
            else if (newValue is Vector2 v2)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Float)
                        {
                            numInput.FloatValue = i == 0 ? v2.X : v2.Y;
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is Vector3 v3)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Float)
                        {
                            switch (i)
                            {
                                case 0:
                                    numInput.FloatValue = v3.X;
                                    break;
                                case 1:
                                    numInput.FloatValue = v3.Y;
                                    break;
                                case 2:
                                    numInput.FloatValue = v3.Z;
                                    break;
                            }
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is Vector4 v4)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Float)
                        {
                            switch (i)
                            {
                                case 0:
                                    numInput.FloatValue = v4.X;
                                    break;
                                case 1:
                                    numInput.FloatValue = v4.Y;
                                    break;
                                case 2:
                                    numInput.FloatValue = v4.Z;
                                    break;
                                case 3:
                                    numInput.FloatValue = v4.W;
                                    break;
                            }
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is Color c)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Int)
                        {
                            switch (i)
                            {
                                case 0:
                                    numInput.IntValue = c.R;
                                    break;
                                case 1:
                                    numInput.IntValue = c.G;
                                    break;
                                case 2:
                                    numInput.IntValue = c.B;
                                    break;
                                case 3:
                                    numInput.IntValue = c.A;
                                    break;
                            }
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
            else if (newValue is Rectangle r)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field is GUINumberInput numInput)
                    {
                        if (numInput.InputType == GUINumberInput.NumberType.Int)
                        {
                            switch (i)
                            {
                                case 0:
                                    numInput.IntValue = r.X;
                                    break;
                                case 1:
                                    numInput.IntValue = r.Y;
                                    break;
                                case 2:
                                    numInput.IntValue = r.Width;
                                    break;
                                case 3:
                                    numInput.IntValue = r.Height;
                                    break;
                            }
                            if (flash)
                            {
                                numInput.Flash(GUI.Style.Green);
                            }
                        }
                    }
                }
            }
        }

        public SerializableEntityEditor(RectTransform parent, ISerializableEntity entity, bool inGame, bool showName, string style = "", int elementHeight = 24, ScalableFont titleFont = null)
            : this(parent, entity, inGame ? SerializableProperty.GetProperties<InGameEditable>(entity) : SerializableProperty.GetProperties<Editable>(entity), showName, style, elementHeight, titleFont)
        {
        }

        public SerializableEntityEditor(RectTransform parent, ISerializableEntity entity, IEnumerable<SerializableProperty> properties, bool showName, string style = "", int elementHeight = 24, ScalableFont titleFont = null)
            : base(style, new RectTransform(Vector2.One, parent))
        {
            this.elementHeight =  (int)(elementHeight * GUI.Scale);
            var tickBoxStyle = GUI.Style.GetComponentStyle("GUITickBox");
            var textBoxStyle = GUI.Style.GetComponentStyle("GUITextBox");
            var numberInputStyle = GUI.Style.GetComponentStyle("GUINumberInput");
            if (tickBoxStyle.Height.HasValue) { this.elementHeight = Math.Max(tickBoxStyle.Height.Value, this.elementHeight); }
            if (textBoxStyle.Height.HasValue) { this.elementHeight = Math.Max(textBoxStyle.Height.Value, this.elementHeight); }
            if (numberInputStyle.Height.HasValue) { this.elementHeight = Math.Max(numberInputStyle.Height.Value, this.elementHeight); }

            layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, RectTransform)) { AbsoluteSpacing = (int)(5 * GUI.Scale) };
            if (showName)
            {
                new GUITextBlock(new RectTransform(new Point(layoutGroup.Rect.Width, this.elementHeight), layoutGroup.RectTransform, isFixedSize: true), entity.Name, font: titleFont ?? GUI.Font)
                {
                    TextColor = Color.White,
                    Color = Color.Black
                };
            }
            properties.ForEach(ep => CreateNewField(ep, entity));

            //scale the size of this component and the layout group to fit the children
            Recalculate();
        }

        public void AddCustomContent(GUIComponent component, int childIndex)
        {
            component.RectTransform.Parent = layoutGroup.RectTransform;
            component.RectTransform.RepositionChildInHierarchy(childIndex);
            layoutGroup.Recalculate();
            Recalculate();
        }

        public void Recalculate() => RectTransform.Resize(new Point(RectTransform.NonScaledSize.X, ContentHeight));

        public GUIComponent CreateNewField(SerializableProperty property, ISerializableEntity entity)
        {
            object value = property.GetValue(entity);
            if (property.PropertyType == typeof(string) && value == null)
            {
                value = "";
            }

            string propertyTag = (entity.GetType().Name + "." + property.PropertyInfo.Name).ToLowerInvariant();
            string fallbackTag = property.PropertyInfo.Name.ToLowerInvariant();
            string displayName = TextManager.Get($"sp.{propertyTag}.name", true, $"sp.{fallbackTag}.name");
            
            if (displayName == null)
            {   
                displayName = property.Name.FormatCamelCaseWithSpaces();
#if DEBUG
                Editable editable = property.GetAttribute<Editable>();
                if (editable != null)
                {
                    if (!MissingLocalizations.Contains($"sp.{propertyTag}.name|{displayName}"))
                    {
                        DebugConsole.NewMessage("Missing Localization for property: " + propertyTag);
                        MissingLocalizations.Add($"sp.{propertyTag}.name|{displayName}");
                        MissingLocalizations.Add($"sp.{propertyTag}.description|{property.GetAttribute<Serialize>().Description}");
                    }
                }
#endif
            }

            string toolTip = TextManager.Get($"sp.{propertyTag}.description", true, !string.IsNullOrEmpty(fallbackTag) ? $"sp.{fallbackTag}.description" : null);

            if (toolTip == null)
            {
                toolTip = property.GetAttribute<Serialize>().Description;
            }

            GUIComponent propertyField = null;
            if (value is bool)
            {
                propertyField = CreateBoolField(entity, property, (bool)value, displayName, toolTip);
            }
            else if (value is string)
            {
                propertyField = CreateStringField(entity, property, (string)value, displayName, toolTip);
            }
            else if (value.GetType().IsEnum)
            {
                if (value.GetType().IsDefined(typeof(FlagsAttribute), inherit: false))
                {
                    propertyField = CreateEnumFlagField(entity, property, value, displayName, toolTip);
                }
                else
                {
                    propertyField = CreateEnumField(entity, property, value, displayName, toolTip);
                }
            }
            else if (value is int i)
            {
                propertyField = CreateIntField(entity, property, i, displayName, toolTip);
            }
            else if (value is float f)
            {
                propertyField = CreateFloatField(entity, property, f, displayName, toolTip);
            }
            else if (value is Point p)
            {
                propertyField = CreatePointField(entity, property, p, displayName, toolTip);
            }
            else if (value is Vector2 v2)
            {
                propertyField = CreateVector2Field(entity, property, v2, displayName, toolTip);
            }
            else if (value is Vector3 v3)
            {
                propertyField = CreateVector3Field(entity, property, v3, displayName, toolTip);
            }
            else if (value is Vector4 v4)
            {
                propertyField = CreateVector4Field(entity, property, v4, displayName, toolTip);
            }
            else if (value is Color c)
            {
                propertyField = CreateColorField(entity, property, c, displayName, toolTip);
            }
            else if (value is Rectangle r)
            {
                propertyField = CreateRectangleField(entity, property, r, displayName, toolTip);
            }
            return propertyField;
        }

        public GUIComponent CreateBoolField(ISerializableEntity entity, SerializableProperty property, bool value, string displayName, string toolTip)
        {
            GUITickBox propertyTickBox = new GUITickBox(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform, isFixedSize: true), displayName)
            {
                Font = GUI.SmallFont,
                Selected = value,
                ToolTip = toolTip,
                OnSelected = (tickBox) =>
                {
                    if (property.TrySetValue(entity, tickBox.Selected))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                    return true;
                }
            };
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { propertyTickBox }); }
            return propertyTickBox;
        }

        public GUIComponent CreateIntField(ISerializableEntity entity, SerializableProperty property, int value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            var editableAttribute = property.GetAttribute<Editable>();
            GUIComponent field;
            if (editableAttribute.ReadOnly)
            {
                var numberInput = new GUITextBlock(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), value.ToString())
                {
                    ToolTip = toolTip,
                    Font = GUI.SmallFont
                };
                field = numberInput as GUIComponent;
            }
            else
            {
                var numberInput = new GUINumberInput(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), GUINumberInput.NumberType.Int)
                {
                    ToolTip = toolTip,
                    Font = GUI.SmallFont
                };
                numberInput.MinValueInt = editableAttribute.MinValueInt;
                numberInput.MaxValueInt = editableAttribute.MaxValueInt;
                numberInput.IntValue = value;
                numberInput.OnValueChanged += (numInput) =>
                {
                    if (property.TrySetValue(entity, numInput.IntValue))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                field = numberInput as GUIComponent;
            }
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { field }); }
            return frame;
        }

        public GUIComponent CreateFloatField(ISerializableEntity entity, SerializableProperty property, float value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - 1, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            
            GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform,
                Anchor.TopRight), GUINumberInput.NumberType.Float)
            {
                ToolTip = toolTip,
                Font = GUI.SmallFont
            };
            var editableAttribute = property.GetAttribute<Editable>();
            numberInput.MinValueFloat = editableAttribute.MinValueFloat;
            numberInput.MaxValueFloat = editableAttribute.MaxValueFloat;
            numberInput.DecimalsToDisplay = editableAttribute.DecimalCount;
            numberInput.valueStep = editableAttribute.ValueStep;
            numberInput.FloatValue = value;

            numberInput.OnValueChanged += (numInput) =>
            {
                if (property.TrySetValue(entity, numInput.FloatValue))
                {
                    numInput.FloatValue = (float)property.GetValue(entity);
                    TrySendNetworkUpdate(entity, property);
                }
            };
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { numberInput }); }
            return frame;
        }

        public GUIComponent CreateEnumField(ISerializableEntity entity, SerializableProperty property, object value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            GUIDropDown enumDropDown = new GUIDropDown(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight),
                elementCount: Enum.GetValues(value.GetType()).Length)
            {
                ToolTip = toolTip
            };
            foreach (object enumValue in Enum.GetValues(value.GetType()))
            {
                enumDropDown.AddItem(enumValue.ToString(), enumValue);
            }
            enumDropDown.SelectItem(value);
            enumDropDown.OnSelected += (selected, val) =>
            {
                if (property.TrySetValue(entity, val))
                {
                    TrySendNetworkUpdate(entity, property);
                }
                return true;
            };
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { enumDropDown }); }
            return frame;
        }

        public GUIComponent CreateEnumFlagField(ISerializableEntity entity, SerializableProperty property, object value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            GUIDropDown enumDropDown = new GUIDropDown(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight),
                elementCount: Enum.GetValues(value.GetType()).Length, selectMultiple: true)
            {
                ToolTip = toolTip
            };
            foreach (object enumValue in Enum.GetValues(value.GetType()))
            {
                enumDropDown.AddItem(enumValue.ToString(), enumValue);
                if (((int)enumValue != 0 || (int)value == 0) && ((Enum)value).HasFlag((Enum)enumValue))
                {
                    enumDropDown.SelectItem(enumValue);
                }
            }
            enumDropDown.OnSelected += (selected, val) =>
            {
                if (property.TrySetValue(entity, string.Join(", ", enumDropDown.SelectedDataMultiple.Select(d => d.ToString()))))
                {
                    TrySendNetworkUpdate(entity, property);
                }
                return true;
            };

            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { enumDropDown }); }
            return frame;
        }

        public GUIComponent CreateStringField(ISerializableEntity entity, SerializableProperty property, string value, string displayName, string toolTip)
        {
            var frame = new GUILayoutGroup(new RectTransform(new Point(Rect.Width, elementHeight), layoutGroup.RectTransform, isFixedSize: true), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont, textAlignment: Alignment.Left)
            {
                ToolTip = toolTip
            };
            string translationTextTag = property.GetAttribute<Serialize>()?.translationTextTag;
            float browseButtonWidth = 0.1f;
            var editableAttribute = property.GetAttribute<Editable>();
            float textBoxWidth = inputFieldWidth;
            if (translationTextTag != null) { textBoxWidth -= browseButtonWidth; }
            GUITextBox propertyBox = new GUITextBox(new RectTransform(new Vector2(textBoxWidth, 1), frame.RectTransform))
            {
                Enabled = editableAttribute != null && !editableAttribute.ReadOnly,
                ToolTip = toolTip,
                Font = GUI.SmallFont,
                Text = value,
                OverflowClip = true,
                OnEnterPressed = (textBox, text) =>
                {
                    if (property.TrySetValue(entity, text))
                    {
                        TrySendNetworkUpdate(entity, property);
                        textBox.Text = (string)property.GetValue(entity);
                        textBox.Deselect();
                    }
                    return true;
                }
            };
            if (translationTextTag != null)
            {
                new GUIButton(new RectTransform(new Vector2(browseButtonWidth, 1), frame.RectTransform, Anchor.TopRight), "...", style: "GUIButtonSmall")
                {
                    OnClicked = (bt, userData) => { CreateTextPicker(translationTextTag, entity, property, propertyBox); return true; }
                };
                propertyBox.OnTextChanged += (tb, text) =>
                {
                    string translatedText = TextManager.Get(text, returnNull: true);
                    if (translatedText == null)
                    {
                        propertyBox.TextColor = Color.Gray;
                        propertyBox.ToolTip = TextManager.GetWithVariable("StringPropertyCannotTranslate", "[tag]", text ?? string.Empty);
                    }
                    else
                    {
                        propertyBox.TextColor = GUI.Style.Green;
                        propertyBox.ToolTip = TextManager.GetWithVariable("StringPropertyTranslate", "[translation]", translatedText);
                    }
                    return true;
                };
                propertyBox.Text = value;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, new GUIComponent[] { propertyBox }); }
            return frame;
        }

        public GUIComponent CreatePointField(ISerializableEntity entity, SerializableProperty property, Point value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            var editableAttribute = property.GetAttribute<Editable>();
            var fields = new GUIComponent[2];
            for (int i = 1; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), inputArea.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.vectorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };

                if (i == 0)
                    numberInput.IntValue = value.X;
                else
                    numberInput.IntValue = value.Y;

                numberInput.MinValueInt = editableAttribute.MinValueInt;
                numberInput.MaxValueInt = editableAttribute.MaxValueInt;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Point newVal = (Point)property.GetValue(entity);
                    if (comp == 0)
                        newVal.X = numInput.IntValue;
                    else
                        newVal.Y = numInput.IntValue;

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                fields[i] = numberInput;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public GUIComponent CreateVector2Field(ISerializableEntity entity, SerializableProperty property, Vector2 value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - inputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(inputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            var editableAttribute = property.GetAttribute<Editable>();
            var fields = new GUIComponent[2];
            for (int i = 1; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), inputArea.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.vectorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

                numberInput.MinValueFloat = editableAttribute.MinValueFloat;
                numberInput.MaxValueFloat = editableAttribute.MaxValueFloat;
                numberInput.DecimalsToDisplay = editableAttribute.DecimalCount;
                numberInput.valueStep = editableAttribute.ValueStep;

                if (i == 0)
                    numberInput.FloatValue = value.X;
                else
                    numberInput.FloatValue = value.Y;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Vector2 newVal = (Vector2)property.GetValue(entity);
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else
                        newVal.Y = numInput.FloatValue;

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                fields[i] = numberInput;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public GUIComponent CreateVector3Field(ISerializableEntity entity, SerializableProperty property, Vector3 value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - largeInputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(largeInputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };
            var editableAttribute = property.GetAttribute<Editable>();
            var fields = new GUIComponent[3];
            for (int i = 2; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.33f, 1), inputArea.RectTransform), style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.vectorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

                numberInput.MinValueFloat = editableAttribute.MinValueFloat;
                numberInput.MaxValueFloat = editableAttribute.MaxValueFloat;
                numberInput.DecimalsToDisplay = editableAttribute.DecimalCount;
                numberInput.valueStep = editableAttribute.ValueStep;

                if (i == 0)
                    numberInput.FloatValue = value.X;
                else if (i == 1)
                    numberInput.FloatValue = value.Y;
                else if (i == 2)
                    numberInput.FloatValue = value.Z;

                int comp = i;
                numberInput.OnValueChanged += (numInput) =>
                {
                    Vector3 newVal = (Vector3)property.GetValue(entity);
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else if (comp == 1)
                        newVal.Y = numInput.FloatValue;
                    else
                        newVal.Z = numInput.FloatValue;

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                fields[i] = numberInput;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public GUIComponent CreateVector4Field(ISerializableEntity entity, SerializableProperty property, Vector4 value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - largeInputFieldWidth, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = toolTip
            };
            var editableAttribute = property.GetAttribute<Editable>();
            var fields = new GUIComponent[4];
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(largeInputFieldWidth, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            for (int i = 3; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform) { MinSize = new Point(50, 0), MaxSize = new Point(150, 50) }, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.vectorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Float)
                {
                    Font = GUI.SmallFont
                };

                numberInput.MinValueFloat = editableAttribute.MinValueFloat;
                numberInput.MaxValueFloat = editableAttribute.MaxValueFloat;
                numberInput.DecimalsToDisplay = editableAttribute.DecimalCount;
                numberInput.valueStep = editableAttribute.ValueStep;

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
                    Vector4 newVal = (Vector4)property.GetValue(entity);
                    if (comp == 0)
                        newVal.X = numInput.FloatValue;
                    else if (comp == 1)
                        newVal.Y = numInput.FloatValue;
                    else if (comp == 2)
                        newVal.Z = numInput.FloatValue;
                    else
                        newVal.W = numInput.FloatValue;

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                fields[i] = numberInput;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public GUIComponent CreateColorField(ISerializableEntity entity, SerializableProperty property, Color value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(1.0f - largeInputFieldWidth, 1), frame.RectTransform) { MinSize = new Point(80, 26) }, displayName, font: GUI.SmallFont)
            {
                ToolTip = displayName + '\n' + toolTip
            };
            label.Text = ToolBox.LimitString(label.Text, label.Font, label.Rect.Width);
            var colorBoxBack = new GUIFrame(new RectTransform(new Vector2(0.04f, 1), frame.RectTransform)
            {
                AbsoluteOffset = new Point(label.Rect.Width, 0)
            }, color: Color.Black, style: null);
            var colorBox = new GUIFrame(new RectTransform(new Vector2(largeInputFieldWidth, 0.9f), colorBoxBack.RectTransform, Anchor.Center), style: null);
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(Math.Max((frame.Rect.Width - label.Rect.Width - colorBoxBack.Rect.Width) / (float)frame.Rect.Width, 0.5f), 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.001f
            };
            var fields = new GUIComponent[4];
            for (int i = 3; i >= 0; i--)
            {
                var element = new GUILayoutGroup(new RectTransform(new Vector2(0.18f, 1), inputArea.RectTransform), isHorizontal: true)
                {
                    Stretch = true
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), element.RectTransform, Anchor.CenterLeft) { MinSize = new Point(15, 0) }, GUI.colorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.Center);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
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
                    Color newVal = (Color)property.GetValue(entity);
                    if (comp == 0)
                        newVal.R = (byte)(numInput.IntValue);
                    else if (comp == 1)
                        newVal.G = (byte)(numInput.IntValue);
                    else if (comp == 2)
                        newVal.B = (byte)(numInput.IntValue);
                    else
                        newVal.A = (byte)(numInput.IntValue);

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                        colorBox.Color = newVal;
                    }
                };
                colorBox.Color = (Color)property.GetValue(entity);
                fields[i] = numberInput;
            }
            frame.RectTransform.MinSize = new Point(0, frame.RectTransform.Children.Max(c => c.MinSize.Y));
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public GUIComponent CreateRectangleField(ISerializableEntity entity, SerializableProperty property, Rectangle value, string displayName, string toolTip)
        {
            var frame = new GUIFrame(new RectTransform(new Point(Rect.Width, Math.Max(elementHeight, 26)), layoutGroup.RectTransform, isFixedSize: true), color: Color.Transparent);
            var label = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1), frame.RectTransform), displayName, font: GUI.SmallFont)
            {
                ToolTip = displayName + '\n' + toolTip
            };
            label.Text = ToolBox.LimitString(label.Text, label.Font, label.Rect.Width);
            var fields = new GUIComponent[4];
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1), frame.RectTransform, Anchor.TopRight), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            for (int i = 3; i >= 0; i--)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.22f, 1), inputArea.RectTransform) { MinSize = new Point(50, 0), MaxSize = new Point(150, 50) }, style: null);
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), GUI.rectComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.Center);
                GUINumberInput numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight),
                    GUINumberInput.NumberType.Int)
                {
                    Font = GUI.SmallFont
                };
                // Not sure if the min value could in any case be negative.
                numberInput.MinValueInt = 0;
                // Just something reasonable to keep the value in the input rect.
                numberInput.MaxValueInt = 9999;

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
                    Rectangle newVal = (Rectangle)property.GetValue(entity);
                    if (comp == 0)
                        newVal.X = numInput.IntValue;
                    else if (comp == 1)
                        newVal.Y = numInput.IntValue;
                    else if (comp == 2)
                        newVal.Width = numInput.IntValue;
                    else
                        newVal.Height = numInput.IntValue;

                    if (property.TrySetValue(entity, newVal))
                    {
                        TrySendNetworkUpdate(entity, property);
                    }
                };
                fields[i] = numberInput;
            }
            if (!Fields.ContainsKey(property.Name)) { Fields.Add(property.Name, fields); }
            return frame;
        }

        public void CreateTextPicker(string textTag, ISerializableEntity entity, SerializableProperty property, GUITextBox textBox)
        {
            var msgBox = new GUIMessageBox("", "", new string[] { TextManager.Get("Cancel") }, new Vector2(0.2f, 0.5f), new Point(300, 400));
            msgBox.Buttons[0].OnClicked = msgBox.Close;

            var textList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.8f), msgBox.Content.RectTransform, Anchor.TopCenter))
            {
                OnSelected = (component, userData) =>
                {
                    string text = userData as string ?? "";

                    if (property.TrySetValue(entity, text))
                    {
                        TrySendNetworkUpdate(entity, property);
                        textBox.Text = (string)property.GetValue(entity);
                        textBox.Deselect();
                    }
                    return true;
                }
            };

            textTag = textTag.ToLowerInvariant();
            var tagTextPairs = TextManager.GetAllTagTextPairs();
            tagTextPairs.Sort((t1, t2) => { return t1.Value.CompareTo(t2.Value); });
            foreach (KeyValuePair<string, string> tagTextPair in tagTextPairs)
            {
                if (!tagTextPair.Key.StartsWith(textTag)) { continue; }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), textList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    ToolBox.LimitString(tagTextPair.Value, GUI.Font, textList.Content.Rect.Width))
                {
                    UserData = tagTextPair.Key
                };
            }
        }
        
        private void TrySendNetworkUpdate(ISerializableEntity entity, SerializableProperty property)
        {
            if (entity is ItemComponent e)
            {
                entity = e.Item;
            }

            if (GameMain.Client != null)
            {
                if (entity is IClientSerializable clientSerializable)
                {
                    GameMain.Client.CreateEntityEvent(clientSerializable, new object[] { NetEntityEvent.Type.ChangeProperty, property });
                }
            }
        }
    }

}
