using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class EditorScreen : Screen
    {
        public static Color BackgroundColor = GameSettings.CurrentConfig.SubEditorBackground;
        public override bool IsEditor => true;

        public override sealed void Deselect()
        {
            DeselectEditorSpecific();
#if !DEBUG
                //reset cheats the player might have used in the editor
                GameMain.LightManager.LightingEnabled = true;
                GameMain.LightManager.LosEnabled = true;
                Hull.EditFire = false;
                Hull.EditWater = false;
                HumanAIController.DisableCrewAI = false;
#endif
        }

        protected virtual void DeselectEditorSpecific() { }

        public void CreateBackgroundColorPicker()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CharacterEditor.EditBackgroundColor"), "", new[] { TextManager.Get("OK") }, new Vector2(0.2f, 0.3f), minSize: new Point(300, 300));
            var rgbLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);

            // Generate R,G,B labels and parent elements
            var layoutParents = new GUILayoutGroup[3];
            for (int i = 0; i < 3; i++)
            {
                var colorContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1), rgbLayout.RectTransform), isHorizontal: true) { Stretch = true };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), colorContainer.RectTransform, Anchor.CenterLeft) { MinSize = new Point(15, 0) }, GUI.ColorComponentLabels[i], font: GUIStyle.SmallFont, textAlignment: Alignment.Center);
                layoutParents[i] = colorContainer;
            }

            // Attach number inputs to our generated parent elements
            var rInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[0].RectTransform), NumberType.Int) { IntValue = BackgroundColor.R };
            var gInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[1].RectTransform), NumberType.Int) { IntValue = BackgroundColor.G };
            var bInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[2].RectTransform), NumberType.Int) { IntValue = BackgroundColor.B };

            rInput.MinValueInt = gInput.MinValueInt = bInput.MinValueInt = 0;
            rInput.MaxValueInt = gInput.MaxValueInt = bInput.MaxValueInt = 255;

            rInput.OnValueChanged = gInput.OnValueChanged = bInput.OnValueChanged = delegate
            {
                var color = new Color(rInput.IntValue, gInput.IntValue, bInput.IntValue);
                BackgroundColor = color;
                var config = GameSettings.CurrentConfig;
                config.SubEditorBackground = color;
                GameSettings.SetCurrentConfig(config);
            };

            // Add RGB picker
            var colorPickerButton = new GUIButton(new RectTransform(new Vector2(0.1f, 1f), layoutParents[0].RectTransform), style: "GUIButtonSmall")
            {
                OnClicked = (button, obj) =>
                {
                    var colorPicker = new GUIColorPicker(new RectTransform(new Vector2(0.5f, 0.5f), msgBox.Content.RectTransform))
                    {
                        CurrentColor = BackgroundColor,
                        OnColorSelected = (component, color) =>
                        {
                            rInput.IntValue = color.R;
                            gInput.IntValue = color.G;
                            bInput.IntValue = color.B;
                            BackgroundColor = color;
                            var config = GameSettings.CurrentConfig;
                            config.SubEditorBackground = color;
                            GameSettings.SetCurrentConfig(config);
                            return true;
                        }
                    };
                    return true;
                }
            };

            // Dropdown for color presets
            var presetDropdown = new GUIDropDown(new RectTransform(new Vector2(1f, 0.1f), msgBox.Content.RectTransform));
            foreach (var preset in GameSettings.CurrentConfig.ColorPresets)
            {
                presetDropdown.AddItem(preset.Key, preset.Key);
            }

            // Delete button
            var deleteButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), msgBox.Content.RectTransform), TextManager.Get("Delete"));
            deleteButton.OnClicked += (button, obj) =>
            {
                var selectedPreset = presetDropdown.SelectedData as string;
                if (selectedPreset != null && selectedPreset != "default")
                {
                    GameSettings.CurrentConfig.ColorPresets.Remove(selectedPreset);
                    GameSettings.SaveCurrentConfig();
                    presetDropdown.ClearChildren();
                    foreach (var preset in GameSettings.CurrentConfig.ColorPresets)
                    {
                        presetDropdown.AddItem(preset.Key, preset.Key);
                    }
                }
                return true;
            };

            presetDropdown.OnSelected += (selected, obj) =>
            {
                var selectedPreset = obj as string;
                if (selectedPreset != null && GameSettings.CurrentConfig.ColorPresets.TryGetValue(selectedPreset, out var color))
                {
                    rInput.IntValue = color.R;
                    gInput.IntValue = color.G;
                    bInput.IntValue = color.B;
                }

                // Disable delete button if "default" is selected
                deleteButton.Enabled = selectedPreset != "default";
                return true;
            };

            // Create button
            var colorNameBox = new GUITextBox(new RectTransform(new Vector2(1f, 0.1f), msgBox.Content.RectTransform));
            var createButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), msgBox.Content.RectTransform), TextManager.Get("Create"));
            createButton.OnClicked += (button, obj) =>
            {
                if (string.IsNullOrWhiteSpace(colorNameBox.Text))
                {
                    colorNameBox.Flash(GUIStyle.Red);
                    return false;
                }
                GameSettings.CurrentConfig.ColorPresets[colorNameBox.Text] = BackgroundColor;
                GameSettings.SaveCurrentConfig();
                presetDropdown.AddItem(colorNameBox.Text, colorNameBox.Text);
                colorNameBox.Text = string.Empty; // Clear the naming field
                return true;
            };

            // Ok button
            msgBox.Buttons[0].RectTransform.RelativeOffset = new Vector2(0, 0.05f); // Move the OK button down
            msgBox.Buttons[0].OnClicked = (button, o) => 
            { 
                msgBox.Close();
                GameSettings.SaveCurrentConfig();
                return true;
            };
        }
    }
}