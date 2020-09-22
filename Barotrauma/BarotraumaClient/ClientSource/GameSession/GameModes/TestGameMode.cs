using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class TestGameMode : GameMode
    {
        public Action OnRoundEnd;

        public bool SpawnOutpost;

        public OutpostGenerationParams OutpostParams;
        public LocationType OutpostType;

        public EventPrefab TriggeredEvent;

        private List<Event> scriptedEvent;

        private GUIButton createEventButton;

        public TestGameMode(GameModePreset preset) : base(preset)
        {
            foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    var variant = Rand.Range(0, jobPrefab.Variants);
                    CrewManager.AddCharacterInfo(new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: jobPrefab, variant: variant));
                }
            }
        }

        public override void Start()
        {
            base.Start();
            
            CrewManager.InitSinglePlayerRound();

            if (SpawnOutpost)
            {
                GenerateOutpost(Submarine.MainSub);
            }

            if (TriggeredEvent != null)
            {
                scriptedEvent = new List<Event> { TriggeredEvent.CreateInstance() };
                GameMain.GameSession.EventManager.PinnedEvent = scriptedEvent.Last();

                createEventButton = new GUIButton(new RectTransform(new Point(128, 64), GUI.Canvas, Anchor.TopCenter) { ScreenSpaceOffset = new Point(0, 32) }, TextManager.Get("create"))
                {
                    OnClicked = delegate 
                    {
                        scriptedEvent.Add(TriggeredEvent.CreateInstance());
                        GameMain.GameSession.EventManager.PinnedEvent = scriptedEvent.Last();
                        return true;
                    }
                };
            }
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            createEventButton?.AddToGUIUpdateList();
        }

        public override void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            GameMain.GameSession.EventManager.PinnedEvent = null;
            OnRoundEnd?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (scriptedEvent != null)
            {
                foreach (Event sEvent in scriptedEvent.Where(sEvent => !sEvent.IsFinished))
                {
                    sEvent.Update(deltaTime);
                }
            }
        }

        private void GenerateOutpost(Submarine submarine)
        {
            Submarine outpost = OutpostGenerator.Generate(OutpostParams ?? OutpostGenerationParams.Params.GetRandom(), OutpostType ?? LocationType.List.GetRandom());
            outpost.SetPosition(Vector2.Zero);

            float closestDistance = 0.0f;
            DockingPort myPort = null, outPostPort = null;
            foreach (DockingPort port in DockingPort.List)
            {
                if (port.IsHorizontal || port.Docked) { continue; }
                if (port.Item.Submarine == outpost)
                {
                    outPostPort = port;
                    continue;
                }
                if (port.Item.Submarine != submarine) { continue; }

                //the submarine port has to be at the top of the sub
                if (port.Item.WorldPosition.Y < submarine.WorldPosition.Y) { continue; }

                float dist = Vector2.DistanceSquared(port.Item.WorldPosition, outpost.WorldPosition);
                if ((myPort == null || dist < closestDistance || port.MainDockingPort) && !(myPort?.MainDockingPort ?? false))
                {
                    myPort = port;
                    closestDistance = dist;
                }
            }

            if (myPort != null && outPostPort != null)
            {
                Vector2 portDiff = myPort.Item.WorldPosition - submarine.WorldPosition;
                Vector2 spawnPos = (outPostPort.Item.WorldPosition - portDiff) - Vector2.UnitY * outPostPort.DockedDistance;

                submarine.SetPosition(spawnPos);
                myPort.Dock(outPostPort);
                myPort.Lock(true);
            }

            if (Character.Controlled != null)
            {
                Character.Controlled.TeleportTo(outpost.GetWaypoints(false).GetRandom(point => point.SpawnType == SpawnType.Human).WorldPosition);
            }
        }
    }
}
