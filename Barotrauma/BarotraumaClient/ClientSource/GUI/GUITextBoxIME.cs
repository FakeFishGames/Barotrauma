using ImeSharp;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma;

/// <summary>
/// A class for handling Input Method Editor (used for inputting e.g. Chinese and Japanese text)
/// </summary>
public partial class GUITextBox : GUIComponent
{
    private static bool initialized;

    public static GUIFrame IMEWindow { get; set; }
    public static GUITextBlock IMETextBlock { get; set; }

    public static void UpdateIME()
    {
        if (!initialized) { InitializeIME(); }
        if (GUI.KeyboardDispatcher.Subscriber is GUITextBox { Selected: true }) 
        {
            IMEWindow?.AddToGUIUpdateList(order: 10);
        }
    }

    private static void InitializeIME()
    {
        InputMethod.Initialize(GameMain.Instance.Window.Hwnd, false);
        InputMethod.TextCompositionCallback = OnTextComposition;
        InputMethod.CommitTextCompositionCallback = OnCommitTextComposition;
        InputMethod.Enabled = true;
        IMEWindow = new GUIFrame(new RectTransform(new Point(GUI.IntScale(300), GUI.IntScale(300)), GUI.Canvas), "InnerFrame") { CanBeFocused = false, Visible = false };
        IMETextBlock = new GUITextBlock(new RectTransform(Vector2.One, IMEWindow.RectTransform), "") { CanBeFocused = false };

        initialized = true;
    }

    private static void OnTextComposition(IMEString compositionText, int cursorPosition, IMEString[] candidateList, int candidatePageStart, int candidatePageSize, int candidateSelection)
    {
        if (GUI.KeyboardDispatcher.Subscriber is not GUITextBox { Selected: true } textBox) { return; }
        IMEWindow.Visible = true;
        string text = compositionText.ToString().Insert(cursorPosition, "|");
        if (candidateList != null)
        {
            text += "\n";
            for (int i = 0; i < candidatePageSize; i++)
            {
                string candidateStr = $"\t{candidatePageStart + i + 1} {candidateList[i]}";
                if (candidateSelection == i)
                {
                    candidateStr = $" ‖color:{XMLExtensions.ToStringHex(Color.White)}‖{candidateStr}‖end‖";
                }
                candidateStr += "\n";
                text += candidateStr;
            }
        }
        IMETextBlock.Text = RichString.Rich(text);

        IMEWindow.RectTransform.NonScaledSize = new Point(
            Math.Max(IMEWindow.Rect.Width, (int)IMETextBlock.TextSize.X + GUI.IntScale(32)),
            (int)IMETextBlock.TextSize.Y);

        Point windowPos = new Point(textBox.Rect.X, textBox.Rect.Bottom);
        if (windowPos.Y + IMEWindow.Rect.Height > GameMain.GraphicsHeight)
        {
            windowPos.Y = textBox.Rect.Y - IMEWindow.Rect.Height;
        }
        IMEWindow.RectTransform.ScreenSpaceOffset = windowPos;
    }

    private static void OnCommitTextComposition(string text)
    {
        if (IMEWindow.Visible)
        {
            foreach (char c in text)
            {
                if (!char.IsControl(c))
                {
                    GUI.KeyboardDispatcher.Subscriber?.ReceiveTextInput(c);
                }
            }
        }
        IMEWindow.Visible = false;
    }
}

