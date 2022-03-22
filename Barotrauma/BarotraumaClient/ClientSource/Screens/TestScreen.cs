#nullable enable
using System;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/*
 * This screen only exists because I'm going mental without access to EnC on Linux.
 * This is fucking stupid and horrible.
 * Remember to remove this crap eventually.
 * - Markus
 */
namespace Barotrauma
{
    class TestScreen : EditorScreen
    {
        public override Camera Cam { get; }

        private Item? miniMapItem;

        private Submarine? submarine;
        private Character? dummyCharacter;
        public static Effect BlueprintEffect;

        private TabMenu tabMenu;

        public TestScreen()
        {
            Cam = new Camera();
            BlueprintEffect = GameMain.GameScreen.BlueprintEffect;

            new GUIButton(new RectTransform(new Point(256, 256), Frame.RectTransform), "Reload shader")
            {
                OnClicked = (button, o) =>
                {
                    BlueprintEffect.Dispose();
                    GameMain.Instance.Content.Unload();
                    BlueprintEffect = GameMain.Instance.Content.Load<Effect>("Effects/blueprintshader_opengl");
                    GameMain.GameScreen.BlueprintEffect = BlueprintEffect;
                    return true;
                }
            };
        }

        public override void Select()
        {
            base.Select();

            if (dummyCharacter is { Removed: false })
            {
                dummyCharacter?.Remove();
            }

            dummyCharacter = Character.Create(CharacterPrefab.HumanSpeciesName, Vector2.Zero, "", id: Entity.DummyID, hasAi: false);
            dummyCharacter.Info.Job = new Job(JobPrefab.Prefabs.Where(jp => TalentTree.JobTalentTrees.ContainsKey(jp.Identifier)).GetRandom());
            dummyCharacter.Info.Name = "Galldren";
            dummyCharacter.Inventory.CreateSlots();

            Character.Controlled = dummyCharacter;
            GameMain.World.ProcessChanges();
            TabMenu.selectedTab = TabMenu.InfoFrameTab.Talents;
            tabMenu = new TabMenu();
        }

        public override void AddToGUIUpdateList()
        {
            Frame.AddToGUIUpdateList();
            CharacterHUD.AddToGUIUpdateList(dummyCharacter);
            dummyCharacter?.SelectedConstruction?.AddToGUIUpdateList();
            tabMenu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            tabMenu.Update();

            if (dummyCharacter is { } dummy)
            {
                dummy.ControlLocalPlayer((float)deltaTime, Cam, false);
                dummy.Control((float)deltaTime, Cam);
            }

            GUI.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            base.Draw(deltaTime, graphics, spriteBatch);
            graphics.Clear(BackgroundColor);

            spriteBatch.Begin(SpriteSortMode.BackToFront, transformMatrix: Cam.Transform);
            miniMapItem?.Draw(spriteBatch, false);
            if (dummyCharacter is { } dummy)
            {
                dummyCharacter.DrawFront(spriteBatch, Cam);
                dummyCharacter.Draw(spriteBatch, Cam);
            }
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState);

            GUI.Draw(Cam, spriteBatch);

            dummyCharacter?.DrawHUD(spriteBatch, Cam, false);

            spriteBatch.End();
        }
    }
}