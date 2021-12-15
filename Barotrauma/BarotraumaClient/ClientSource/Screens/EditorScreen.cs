using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class EditorScreen : Screen
    {
        public static Color BackgroundColor = GameSettings.SubEditorBackgroundColor;

        public void CreateBackgroundColorPicker()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CharacterEditor.EditBackgroundColor"), "", new[] { TextManager.Get("Reset"), TextManager.Get("OK")}, new Vector2(0.2f, 0.175f), minSize: new Point(300, 175));

            var rgbLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), msgBox.Content.RectTransform), isHorizontal: true);

            // Generate R,G,B labels and parent elements
            var layoutParents = new GUILayoutGroup[3];
            for (int i = 0; i < 3; i++)
            {
                var colorContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1), rgbLayout.RectTransform), isHorizontal: true) { Stretch = true };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1), colorContainer.RectTransform, Anchor.CenterLeft) { MinSize = new Point(15, 0) }, GUI.colorComponentLabels[i], font: GUI.SmallFont, textAlignment: Alignment.Center);
                layoutParents[i] = colorContainer;
            }

            // attach number inputs to our generated parent elements
            var rInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[0].RectTransform), GUINumberInput.NumberType.Int) { IntValue = BackgroundColor.R };
            var gInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[1].RectTransform), GUINumberInput.NumberType.Int) { IntValue = BackgroundColor.G };
            var bInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1f), layoutParents[2].RectTransform), GUINumberInput.NumberType.Int) { IntValue = BackgroundColor.B };

            rInput.MinValueInt = gInput.MinValueInt = bInput.MinValueInt = 0;
            rInput.MaxValueInt = gInput.MaxValueInt = bInput.MaxValueInt = 255;
            
            rInput.OnValueChanged = gInput.OnValueChanged = bInput.OnValueChanged = delegate
            {
                var color = new Color(rInput.IntValue, gInput.IntValue, bInput.IntValue);
                BackgroundColor = color;
                GameSettings.SubEditorBackgroundColor = color;
            };
            
            // Reset button
            msgBox.Buttons[0].OnClicked = (button, o) =>
            {
                rInput.IntValue = 13;
                gInput.IntValue = 37;
                bInput.IntValue = 69;
                return true;
            };

            // Ok button
            msgBox.Buttons[1].OnClicked = (button, o) => 
            { 
                msgBox.Close();
                GameMain.Config.SaveNewPlayerConfig();
                return true;
            };
        }
    }
}