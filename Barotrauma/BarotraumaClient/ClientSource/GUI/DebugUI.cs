using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public static class DebugUI
    {
        public static Dictionary<string, Action> OpenableWindows => new Dictionary<string, Action>()
        {
            { "explorer", () => CreateObjectExplorer() },
            { "inspector", () => inspectorTooltip.Visible = true },
            // TODO: Debug infos/overlays toggle window, GUI inspectors/editors
        };

        private static readonly GUITextBlock inspectorTooltip = CreateObjectInspector();
        private static readonly List<(GUIFrame window, GUIListBox list)> explorerWindows = new List<(GUIFrame, GUIListBox)>();
        private static readonly Dictionary<ISerializableEntity, (GUIFrame window, GUIListBox list)> editorWindows = new Dictionary<ISerializableEntity, (GUIFrame, GUIListBox)>();

        private static IEnumerable<ISerializableEntity> Entities => Entity.GetEntities().Where(e => e is ISerializableEntity s).Select(e => e as ISerializableEntity);
        private static Vector2 CursorPosWorld => Screen.Selected.Cam.ScreenToWorld(PlayerInput.LatestMousePosition);
        private static IEnumerable<ISerializableEntity> EntitiesUnderCursor => Entities.Where(e => e switch
        {
            MapEntity m => m.WorldRect.ContainsWorld(CursorPosWorld),
            Character c => new Rectangle(c.AnimController.Collider.SimPosition.ToPoint(), c.AnimController.Collider.GetSize().ToPoint()).ContainsWorld(CursorPosWorld),
            _ => false
        });

        public static void Open(string window) => OpenableWindows[window].Invoke();
        public static void Close()
        {
            inspectorTooltip.Visible = false;
            explorerWindows.Clear();
            editorWindows.Clear();
        }

        private static void CreateObjectExplorer(IEnumerable<ISerializableEntity> entities = null)
        {
            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);
            GUIButton entityListRefresh = null;

            if (entities is null)
            {
                entityListRefresh = new(new(new Vector2(1, 0.1f), content.RectTransform))
                {
                    Text = TextManager.Get("ReloadLinkedSub")
                };
            }

            GUILayoutGroup nameFilterArea = new(new(new Vector2(1, 0.1f), content.RectTransform), true)
            {
                Stretch = true
            };

            GUITextBlock nameFilterLabel = new(new(Vector2.UnitY, nameFilterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.Font);
            GUITextBox nameFilterBox = new(new(Vector2.One, nameFilterArea.RectTransform), font: GUIStyle.Font, createClearButton: true);
            nameFilterBox.OnSelected += (sender, userdata) => nameFilterLabel.Visible = false;
            nameFilterBox.OnDeselected += (sender, userdata) => nameFilterLabel.Visible = true;

            GUIListBox entityList = new(new(new Vector2(1, entities is null ? 0.7f : 0.8f), content.RectTransform));
            entityList.OnSelected += (component, obj) =>
            {
                if (Entity.GetEntities().Contains(obj) && obj is ISerializableEntity entity)
                {
                    if (editorWindows.ContainsKey(entity))
                    {
                        editorWindows[entity].window.Flash(GUIStyle.Green);
                        return false;
                    }
                    else
                    {
                        return TryCreateEditorWindow(entity);
                    }
                }
                else
                {
                    component.RectTransform.Parent = null;
                    return false;
                }
            };

            nameFilterBox.OnTextChanged += (textBox, text) =>
            {
                FilterEntries(entityList, nameFilterBox.Text);
                return true;
            };

            GUIButton closeButton = new(new(new Vector2(1, 0.1f), content.RectTransform))
            {
                Text = TextManager.Get("Close"),
                OnClicked = (component, obj) =>
                {
                    explorerWindows.Remove((window, entityList));
                    window.RectTransform.Parent = null;
                    return true;
                }
            };

            if (entities is null)
            {
                entityListRefresh.OnClicked = (button, obj) =>
                {
                    RecreateEntityList(entityList, entities ?? Entities);
                    FilterEntries(entityList, nameFilterBox.Text);
                    return true;
                };
            }

            explorerWindows.Add((window, entityList));
            RecreateEntityList(entityList, entities ?? Entities);
        }

        private static GUITextBlock CreateObjectInspector()
        {
            return new(new(new Point(200, 50), GUI.Canvas), "", textAlignment: Alignment.TopLeft)
            {
                Visible = false,
                CanBeFocused = false
            };
        }

        private static bool TryCreateEditorWindow(ISerializableEntity entity)
        {
            GUILayoutGroup content = CreateWindowBase(out GUIFrame window);

            GUIButton editorListRefresh = new(new(new Vector2(1, 0.1f), content.RectTransform))
            {
                Text = TextManager.Get("ReloadLinkedSub"),
                OnClicked = (button, obj) =>
                {
                    RefreshEditor(entity);
                    return true;
                }
            };

            GUIListBox editorList = new(new(new Vector2(1, 0.8f), content.RectTransform));

            GUIButton closeButton = new(new(new Vector2(1, 0.1f), content.RectTransform))
            {
                Text = TextManager.Get("Close"),
                OnClicked = (component, obj) =>
                {
                    editorWindows.Remove(entity);
                    window.RectTransform.Parent = null;
                    return true;
                }
            };

            if (editorWindows.TryAdd(entity, new(window, editorList)))
            {
                RefreshEditor(entity);
                return true;
            }
            else
            {
                return false;
            }
        }

        private static GUILayoutGroup CreateWindowBase(out GUIFrame window)
        {
            window = new(new(new Vector2(0.25f, 0.5f), GUI.Canvas, Anchor.Center))
            {
                CanBeFocused = false
            };

            GUIDragHandle handle = new(new(Vector2.One, window.RectTransform, Anchor.Center), window.RectTransform, null);

            int dragIconHeight = GUIStyle.ItemFrameMargin.Y / 4;
            new GUIImage(new(new Point(window.Rect.Width, dragIconHeight), handle.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, dragIconHeight / 2), MinSize = new Point(0, dragIconHeight) }, "GUIDragIndicatorHorizontal")
            {
                CanBeFocused = false
            };

            return new(new(window.Rect.Size - GUIStyle.ItemFrameMargin, window.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset })
            {
                CanBeFocused = false
            };
        }

        private static void RecreateEntityList(GUIListBox list, IEnumerable<ISerializableEntity> entities)
        {
            list.ClearChildren();
            foreach (ISerializableEntity entity in entities)
            {
                GUITextBlock entry = new(new(new Vector2(1, 0.05f), list.Content.RectTransform), RichString.Rich($"{entity.Name} (‖color:gui.green‖{entity.GetType().Name}‖end‖) {(entity as Entity).WorldPosition}"), Color.White)
                {
                    Padding = Vector4.Zero,
                    UserData = entity
                };

                new GUITextBlock(new(Vector2.One, entry.RectTransform), (entity as Entity).ID.ToString(), Color.Gray, textAlignment: Alignment.Right)
                {
                    CanBeFocused = false
                };
            }
        }

        private static void FilterEntries(GUIListBox list, string filter) => list.Content.GetAllChildren<GUITextBlock>().Where(e => e.Parent == list.Content).ForEach(i => i.Visible = i.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));

        private static void RefreshEditor(ISerializableEntity entity)
        {
            editorWindows[entity].list.ClearChildren();
            GUIFrame content = editorWindows[entity].list.Content;

            new SerializableEntityEditor(content.RectTransform, entity, false, true);
            if (entity is Item item)
            {
                item.Components.ForEach(component => new SerializableEntityEditor(content.RectTransform, component, false, true));
            }

            if (entity is Character character)
            {
                new SerializableEntityEditor(content.RectTransform, character.Params, false, true);
                new SerializableEntityEditor(content.RectTransform, character.AnimController.RagdollParams, false, true);
                character.AnimController.Limbs.ForEach(limb =>
                {
                    new SerializableEntityEditor(content.RectTransform, limb, false, true);
                    limb.DamageModifiers.ForEach(mod => new SerializableEntityEditor(content.RectTransform, mod, false, true));
                });
            }
        }

        public static void Update()
        {
            foreach ((GUIFrame window, GUIListBox) window in explorerWindows)
            {
                window.window.AddToGUIUpdateList();
            }

            foreach (KeyValuePair<ISerializableEntity, (GUIFrame window, GUIListBox)> window in editorWindows)
            {
                if (!Entities.Contains(window.Key))
                {
                    editorWindows.Remove(window.Key);
                    continue;
                }

                window.Value.window.AddToGUIUpdateList();
            }

            if (inspectorTooltip.Visible)
            {
                if (PlayerInput.SecondaryMouseButtonClicked())
                {
                    inspectorTooltip.Visible = false;
                }
                else if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    CreateObjectExplorer(EntitiesUnderCursor);
                    inspectorTooltip.Visible = false;
                }
                else
                {
                    inspectorTooltip.RectTransform.AbsoluteOffset = PlayerInput.LatestMousePosition.ToPoint() + new Point(20, 0);
                    inspectorTooltip.Text = $"Inspector mode (RMB to cancel)\nCursor pos: {CursorPosWorld}\nEntities below cursor: {EntitiesUnderCursor.Count()}";
                    EntitiesUnderCursor.ForEach(e => inspectorTooltip.Text += $"\n- {e.Name}");
                    inspectorTooltip.AddToGUIUpdateList();
                }
            }
        }
    }
}