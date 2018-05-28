using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;

namespace Barotrauma
{
    class AnimationEditorScreen : Screen
    {
        private Camera cam;
        public override Camera Cam
        {
            get
            {
                if (cam == null)
                {
                    cam = new Camera();
                }
                return cam;
            }
        }

        private Character _character;
        private Vector2 spawnPosition;

        public override void Select()
        {
            base.Select();
            Submarine.RefreshSavedSubs();
            Submarine.MainSub = Submarine.SavedSubmarines.First(s => s.Name.Contains("AnimEditor"));
            Submarine.MainSub.Load(true);
            CalculateMovementLimits();
            _character = SpawnCharacter(Character.HumanConfigFile);
            AnimParams.ForEach(p => p.AddToEditor());
            CreateButtons();
        }

        #region Inifinite runner
        private int min;
        private int max;
        private void CalculateMovementLimits()
        {
            min = CurrentWalls.Select(w => w.Rect.Left).OrderBy(p => p).First();
            max = CurrentWalls.Select(w => w.Rect.Right).OrderBy(p => p).Last();
        }

        private List<Structure> _originalWalls;
        private List<Structure> OriginalWalls
        {
            get
            {
                if (_originalWalls == null)
                {
                    _originalWalls = Structure.WallList;
                }
                return _originalWalls;
            }
        }

        private List<Structure> clones = new List<Structure>();
        private List<Structure> previousWalls;

        private List<Structure> _currentWalls;
        private List<Structure> CurrentWalls
        {
            get
            {
                if (_currentWalls == null)
                {
                    _currentWalls = OriginalWalls;
                }
                return _currentWalls;
            }
            set
            {
                _currentWalls = value;
            }
        }

        private void CloneWalls(bool right)
        {
            previousWalls = CurrentWalls;
            if (previousWalls == null)
            {
                previousWalls = OriginalWalls;
            }
            if (clones.None())
            {
                OriginalWalls.ForEachMod(w => clones.Add(w.Clone() as Structure));
                CurrentWalls = clones;
            }
            else
            {
                // Select by position
                var lastWall = right ?
                    previousWalls.OrderBy(w => w.Rect.Right).Last() :
                    previousWalls.OrderBy(w => w.Rect.Left).First();

                CurrentWalls = clones.Contains(lastWall) ? clones : OriginalWalls;
            }
            if (CurrentWalls != OriginalWalls)
            {
                // Move the clones
                for (int i = 0; i < CurrentWalls.Count; i++)
                {
                    int amount = right ? previousWalls[i].Rect.Width : -previousWalls[i].Rect.Width;
                    CurrentWalls[i].Move(new Vector2(amount, 0));
                }
            }
            GameMain.World.ProcessChanges();
            CalculateMovementLimits();
        }
        #endregion

        #region Character spawning
        private int characterIndex = -1;
        private List<string> allFiles;
        private List<string> AllFiles
        {
            get
            {
                if (allFiles == null)
                {
                    allFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.Character).FindAll(f => !f.Contains("husk"));
                    allFiles.ForEach(f => DebugConsole.NewMessage(f, Color.White));
                }
                return allFiles;
            }
        }

        private string GetNextConfigFile()
        {
            if (characterIndex == -1)
            {
                characterIndex = AllFiles.IndexOf(GetConfigFile(_character.SpeciesName));
            }
            characterIndex++;
            if (characterIndex == AllFiles.Count - 1)
            {
                characterIndex = 0;
            }
            return AllFiles[characterIndex];
        }

        private string GetConfigFile(string speciesName)
        {
            return AllFiles.Find(c => c.EndsWith(speciesName + ".xml"));
        }

        private Character SpawnCharacter(string configFile)
        {
            DebugConsole.NewMessage($"Trying to spawn {configFile}", Color.HotPink);
            spawnPosition = WayPoint.GetRandom(sub: Submarine.MainSub).WorldPosition;
            var character = Character.Create(configFile, spawnPosition, ToolBox.RandomSeed(8), hasAi: false);
            character.Submarine = Submarine.MainSub;
            character.AnimController.forceStanding = character.IsHumanoid;
            Character.Controlled = character;
            float size = ConvertUnits.ToDisplayUnits(character.AnimController.Collider.radius * 2);
            float margin = 100;
            float distance = Vector2.Distance(spawnPosition, new Vector2(spawnPosition.X, OriginalWalls.First().WorldPosition.Y)) - margin;
            if (size > distance)
            {
                character.AnimController.Teleport(ConvertUnits.ToSimUnits(new Vector2(0, size * 1.5f)), Vector2.Zero);
            }
            Cam.Position = character.WorldPosition;
            Cam.TargetPos = character.WorldPosition;
            Cam.UpdateTransform(true);
            GameMain.World.ProcessChanges();
            return character;
        }
        #endregion

        #region GUI
        private GUIFrame frame;
        private void CreateButtons()
        {
            if (frame != null)
            {
                frame.RectTransform.Parent = null;
            }
            frame = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.9f), parent: Frame.RectTransform, anchor: Anchor.CenterRight) { RelativeOffset = new Vector2(0.01f, 0) });
            var layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform));
            var switchCharacterButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Switch Character");
            switchCharacterButton.OnClicked += (b, obj) =>
            {
                _character = SpawnCharacter(GetNextConfigFile());
                ResetEditor();
                CreateButtons();
                return true;
            };
            // TODO: use tick boxes?
            var swimButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), _character.AnimController.forceStanding ? "Swim" : "Stand");
            swimButton.OnClicked += (b, obj) =>
            {
                _character.AnimController.forceStanding = !_character.AnimController.forceStanding;
                swimButton.Text = _character.AnimController.forceStanding ? "Swim" : "Stand";
                return true;
            };
            var moveButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), _character.OverrideMovement.HasValue ? "Stop" : "Move");
            moveButton.OnClicked += (b, obj) =>
            {
                _character.OverrideMovement = _character.OverrideMovement.HasValue ? null : new Vector2(1, 0) as Vector2?;
                moveButton.Text = _character.OverrideMovement.HasValue ? "Stop" : "Move";
                return true;
            };
            var runButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), _character.ForceRun ? "Walk" : "Run");
            runButton.OnClicked += (b, obj) =>
            {
                _character.ForceRun = !_character.ForceRun;
                runButton.Text = _character.ForceRun ? "Walk" : "Run";
                return true;
            };
            var saveButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Save");
            saveButton.OnClicked += (b, obj) =>
            {
                AnimParams.ForEach(p => p.Save());
                return true;
            };
            var resetButton = new GUIButton(new RectTransform(new Vector2(1, 0.1f), layoutGroup.RectTransform), "Reset");
            resetButton.OnClicked += (b, obj) =>
            {
                AnimParams.ForEach(p => p.Reset());
                ResetEditor();
                return true;
            };
        }
        #endregion

        #region AnimParams
        private IEnumerable<Animation> AnimParams
        {
            get
            {
                if (_character.IsHumanoid)
                {
                    return new List<Animation> { HumanoidGroundedAnimation.WalkInstance, HumanoidGroundedAnimation.RunInstance };
                }
                else
                {
                    var animParams = new List<Animation>();
                    var instance = FishSwimAnimation.GetAnimParamsFromSpeciesName(_character.SpeciesName);
                    if (instance != null)
                    {
                        animParams.Add(instance);
                    }
                    return animParams;
                }
            }
        }

        private void ResetEditor()
        {
            Barotrauma.Animation.CreateEditor();
            AnimParams.ForEach(p => p.AddToEditor());
        }
        #endregion

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            Barotrauma.Animation.Editor.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            Submarine.MainSub.SetPrevTransform(Submarine.MainSub.Position);
            Submarine.MainSub.Update((float)deltaTime);

            //Vector2 mouseSimPos = ConvertUnits.ToSimUnits(character.CursorPosition);
            //foreach (Limb limb in character.AnimController.Limbs)
            //{
            //    limb.body.SetTransform(mouseSimPos, 0.0f);
            //}
            //character.AnimController.Collider.SetTransform(mouseSimPos, 0.0f);

            PhysicsBody.List.ForEach(pb => pb.SetPrevTransform(pb.SimPosition, pb.Rotation));

            _character.ControlLocalPlayer((float)deltaTime, Cam, false);
            _character.Control((float)deltaTime, Cam);
            _character.AnimController.UpdateAnim((float)deltaTime);
            _character.AnimController.Update((float)deltaTime, Cam);

            // Teleports the character -> not very smooth
            //if (_character.Position.X < min || _character.Position.X > max)
            //{
            //    _character.AnimController.SetPosition(ConvertUnits.ToSimUnits(new Vector2(spawnPosition.X, _character.Position.Y)), false);
            //}

            if (_character.Position.X < min)
            {
                CloneWalls(false);
            }
            else if (_character.Position.X > max)
            {
                CloneWalls(true);
            }

            Cam.MoveCamera((float)deltaTime);
            Cam.Position = _character.Position;

            GameMain.World.Step((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            base.Draw(deltaTime, graphics, spriteBatch);
            graphics.Clear(Color.CornflowerBlue);
            Cam.UpdateTransform(true);

            // Submarine
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            Submarine.Draw(spriteBatch, true);
            spriteBatch.End();

            // Character
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: Cam.Transform);
            _character.Draw(spriteBatch);
            spriteBatch.End();

            // GUI
            spriteBatch.Begin(SpriteSortMode.Immediate, rasterizerState: GameMain.ScissorTestEnable);
            GUI.Draw((float)deltaTime, spriteBatch);

            // Debug
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 00), $"Cursor World Pos: {_character.CursorWorldPosition}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 20), $"Cursor Pos: {_character.CursorPosition}", Color.White, font: GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 60), $"Character World Pos: {_character.WorldPosition}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 80), $"Character Pos: {_character.Position}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 100), $"Character Sim Pos: {_character.SimPosition}", Color.White, font: GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 140), $"Submarine World Pos: {Submarine.MainSub.WorldPosition}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 160), $"Submarine Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 180), $"Submarine Sim Pos: {Submarine.MainSub.Position}", Color.White, font: GUI.SmallFont);

            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 220), $"Movement Limits: MIN: {min} MAX: {max}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 240), $"Clones: {clones.Count}", Color.White, font: GUI.SmallFont);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, 260), $"Total amount of walls: {Structure.WallList.Count}", Color.White, font: GUI.SmallFont);

            spriteBatch.End();
        }
    }
}
