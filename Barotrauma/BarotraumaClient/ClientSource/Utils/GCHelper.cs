using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public partial class GCHelper
    {
        public static void TryDrawActive(SpriteBatch spriteBatch)
        {
            if (!active.TryUnwrap(out var helper)) { return; }

            helper.Draw(spriteBatch);
        }

        private static readonly GUIFont Font = GUIStyle.Font;

        private int scrollIndex;

        private bool showLowPriorityObjects;

        private readonly MultiGraph gcGraph = new(dimensions: GC.MaxGeneration + 1,
                                                  arraySize: 30,
                                                  tooltipFmt: "GC Collections: {0}");

        public static void TryUpdateActive()
        {
            if (!active.TryUnwrap(out var helper)) { return; }
            helper.Update();
        }

        private void Update()
        {
            if (PlayerInput.ScrollWheelSpeed != 0)
            {
                int amount = PlayerInput.IsShiftDown() ? 10 : 1;
                scrollIndex = PlayerInput.ScrollWheelSpeed < 0
                                  ? scrollIndex + amount
                                  : scrollIndex - amount;
            }

            if (PlayerInput.IsShiftDown())
            {
                if (PlayerInput.KeyHit(Keys.D0))
                {
                    GC.Collect();
                }

                if (PlayerInput.KeyHit(Keys.D1))
                {
                    GC.Collect(generation: 0, mode: GCCollectionMode.Forced, blocking: true);
                }

                if (PlayerInput.KeyHit(Keys.D2))
                {
                    GC.Collect(generation: 1, mode: GCCollectionMode.Forced, blocking: true);
                }

                if (PlayerInput.KeyHit(Keys.D3))
                {
                    GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true);
                }

                if (PlayerInput.KeyHit(Keys.L))
                {
                    showLowPriorityObjects = !showLowPriorityObjects;
                }
            }
        }

        private void Draw(SpriteBatch spriteBatch)
        {
            float currentY = 10f;
            float currentX = GameMain.GraphicsWidth - 500f;

            string tooltip = null;
            GCMemoryInfo gcMemoryInfo = GC.GetGCMemoryInfo();
            DrawLine("GCHelper - Garbage Collection Helper");
            Padding();
            DrawLine($"Memory Usage: {ToMegaBytes(GC.GetTotalMemory(forceFullCollection: false))} MB");

            DrawLine($"GC Latency Mode: {GCSettings.LatencyMode}");
            DrawLine($"Generation: {gcMemoryInfo.Generation}/{GC.MaxGeneration}");
            DrawLine($"Heap Size: {ToMegaBytes(gcMemoryInfo.HeapSizeBytes)} MB");
            DrawLine($"Fragmentation: {ToMegaBytes(gcMemoryInfo.FragmentedBytes)} MB");
            DrawLine($"Pending Finalizers: {gcMemoryInfo.FinalizationPendingCount}");
            Padding();

            RectangleF graphHeaderRect = DrawLine("GC Collections:");
            if (graphHeaderRect.Contains(PlayerInput.MousePosition))
            {
                tooltip = "Note: This is how many times the garbage collection has ran.\n" +
                          "Not how many objects were collected.";
            }

            RectangleF graphRect = new RectangleF(currentX - 300f, currentY, 300f, 100f);
            Span<Color> colors = [ColorForGeneration(0), ColorForGeneration(1), ColorForGeneration(2)];

            currentY += graphRect.Height;

            for (int i = 0; i <= GC.MaxGeneration; i++)
            {
                Color color = ColorForGeneration(i);

                RectangleF rect = DrawLine($"‖color:{color.ToStringHex()}‖{LabelForGeneration(i)}‖end‖ Count: {GC.CollectionCount(i)}");

                if (rect.Contains(PlayerInput.MousePosition))
                {
                    colors.Fill(Color.Transparent);
                    colors[i] = color;

                    tooltip = $"How many times the garbage collection has ran on generation {i}";
                }
            }

            gcGraph.Draw(spriteBatch, graphRect, minimumValue: 5f, colors: colors, outlineThickness: 3f);

            Padding();

            RectangleF keyBindRect = DrawLine("‖color:gui.yellow‖Hover for keybind info‖end‖");

            if (keyBindRect.Contains(PlayerInput.MousePosition))
            {
                tooltip = "Press ‖color:gui.orange‖Shift + 0‖end‖ to collect all generations.\n" +
                          "Press ‖color:gui.orange‖Shift + 1‖end‖ to collect generation 0.\n" +
                          "Press ‖color:gui.orange‖Shift + 2‖end‖ to collect generation 1.\n" +
                          "Press ‖color:gui.orange‖Shift + 3‖end‖ to collect generation 2.\n" +
                          "Press ‖color:gui.orange‖Shift + L‖end‖ to toggle showing low priority objects.\n" +
                          "‖color:gui.orange‖Scroll wheel‖end‖ to scroll through tracked objects.";
            }

            Padding();

            IEnumerable<TrackedObject> objectsToShow =
                !showLowPriorityObjects
                    ? trackedObjects.Where(static obj => obj.Priority is not WatchPriority.Low)
                    : trackedObjects;

            int count = objectsToShow.Count();

            scrollIndex = Math.Clamp(scrollIndex, min: 0, max: count);
            lock (lockObj)
            {
                DrawLine($"Tracked Objects: {count} Offset: {scrollIndex} Low Priority: {(showLowPriorityObjects ? "Shown" : "Hidden")}");
                if (scrollIndex > 0)
                {
                    DrawLine("‖color:gui.yellow‖Scroll up to see previous objects‖end‖");
                }

                int toSkip = scrollIndex;
                foreach (TrackedObject trackedObject in objectsToShow)
                {
                    if (toSkip > 0)
                    {
                        toSkip--;
                        continue;
                    }

                    if (currentY + Font.LineHeight > GameMain.GraphicsHeight - Font.LineHeight)
                    {
                        DrawLine("‖color:gui.yellow‖Scroll down to see more objects‖end‖");
                        break;
                    }

                    string line = " - ";
                    if (trackedObject.IsAlive)
                    {
                        line += "[‖color:gui.green‖Alive‖end‖] ";
                    }
                    else
                    {
                        line += "[‖color:gui.red‖Collected‖end‖] ";
                    }

                    if (trackedObject.Priority is WatchPriority.Low)
                    {
                        line += $"‖color:{Color.DarkGray.ToStringHex()}‖{trackedObject.ObjectName}‖end‖";
                    }
                    else
                    {
                        line += $"{trackedObject.ObjectName}";
                    }

                    RectangleF rect = DrawLine(line);
                    if (rect.Contains(PlayerInput.MousePosition))
                    {
                        ulong address = trackedObject.Address;
                        bool copyToClipboard = PlayerInput.KeyDown(Keys.C);

                        DateTimeOffset currentTime = DateTimeOffset.Now;
                        TimeSpan age = currentTime - trackedObject.CreatedAt;
                        string text = $"Created: {age.Hours:00}:{age.Minutes:00}:{age.Seconds:00} ago\n" +
                                      $"Address: ‖color:{(copyToClipboard ? "gui.yellow" : "gui.orange")}‖0x{address:X}‖end‖ (Press {Keys.C} to copy to clipboard)\n" +
                                      $"Note: {trackedObject.Notes}\n";

                        text += "Creation Stack Trace: ";
                        if (PlayerInput.IsShiftDown())
                        {
                            text += $"\n{trackedObject.StackTrace}";
                        }
                        else
                        {
                            text += "(Hold Shift to view)";
                        }

                        if (copyToClipboard)
                        {
                            Clipboard.SetText($"0x{address:X}");
                        }

                        tooltip = text;
                    }
                }
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                RichString tooltipRichText = RichString.Rich(tooltip);
                Vector2 textSize = Font.MeasureString(tooltipRichText.SanitizedString);
                Vector2 tooltipOffset = new Vector2(20f, 35f) * GUI.Scale;
                RectangleF tooltipRect = new RectangleF(PlayerInput.MousePosition + tooltipOffset, textSize);
                tooltipRect.Inflate(10f, 10f);

                if (tooltipRect.Right > GameMain.GraphicsWidth)
                {
                    tooltipRect.X = GameMain.GraphicsWidth - tooltipRect.Width;
                }

                if (tooltipRect.Bottom > GameMain.GraphicsHeight)
                {
                    tooltipRect.Y = GameMain.GraphicsHeight - tooltipRect.Height;
                }

                GUI.DrawFilledRectangle(spriteBatch, tooltipRect, Color.Black * 0.8f);
                Font.DrawStringWithColors(spriteBatch, tooltipRichText.SanitizedValue,
                                          position: tooltipRect.Location + new Vector2(10f),
                                          color: Color.White,
                                          rotation: 0f,
                                          origin: Vector2.Zero,
                                          scale: 1f,
                                          spriteEffects: SpriteEffects.None,
                                          layerDepth: 0f,
                                          richTextData: tooltipRichText.RichTextData);
            }

            return;

            RectangleF DrawLine(string line)
            {
                RichString text = RichString.Rich(line);
                Vector2 stringSize = Font.MeasureString(text.SanitizedValue);

                Vector2 position = new Vector2(currentX - stringSize.X, currentY);
                RectangleF rect = new RectangleF(position, stringSize);
                GUI.DrawFilledRectangle(spriteBatch, rect, Color.Black * 0.8f);
                Font.DrawStringWithColors(sb: spriteBatch,
                                          text: text.SanitizedValue,
                                          position: position,
                                          color: Color.White,
                                          rotation: 0f,
                                          origin: Vector2.Zero,
                                          scale: 1f,
                                          spriteEffects: SpriteEffects.None,
                                          layerDepth: 0f,
                                          text.RichTextData,
                                          alignment: Alignment.Left);
                currentY += Font.LineHeight;
                return rect;
            }

            void Padding()
                => currentY += Font.LineHeight;
        }

        private static Color ColorForGeneration(int generation)
            => generation switch
            {
                0 => Color.LightGreen,
                1 => Color.LightBlue,
                2 => Color.LightPink,
                _ => Color.White
            };

        private static string LabelForGeneration(int generation)
            => generation switch
            {
                0 => "New",
                1 => "Short-Lived",
                2 => "Long-Lived",
                _ => $"Generation {generation}"
            };
    }
}