using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Barotrauma
{
    class NilModLagDiagnostics
    {
        //Variables for storing the LAST time we checked it for

        public const int MaximumSamples = 25;
        public const float UpdateRate = 0.25f;
        public float UpdateTimer = 0f;

        //Each stopwatch with its own variables and recording code

        //Method to update how often we re-calculate the average value, should make the values change far less often.
        public void Update(float DeltaTime)
        {
            UpdateTimer += DeltaTime;
            if (UpdateTimer >= UpdateRate)
            {
                if (SampleBufferMainUpdateLoop.Count > 0)
                {
                    AverageMainUpdateLoop = SampleBufferMainUpdateLoop.Average(i => i);
                }
                if (SampleBufferGUIUpdate.Count > 0)
                {
                    AverageGUIUpdate = SampleBufferGUIUpdate.Average(i => i);
                }
                if (SampleBufferDebugConsole.Count > 0)
                {
                    AverageDebugConsole = SampleBufferDebugConsole.Average(i => i);
                }
                if (SampleBufferPlayerInput.Count > 0)
                {
                    AveragePlayerInput = SampleBufferPlayerInput.Average(i => i);
                }
                if (SampleBufferSoundPlayer.Count > 0)
                {
                    AverageSoundPlayer = SampleBufferSoundPlayer.Average(i => i);
                }
                if (SampleBufferNetworkMember.Count > 0)
                {
                    AverageNetworkMember = SampleBufferNetworkMember.Average(i => i);
                }
                if (SampleBufferCoroutineManager.Count > 0)
                {
                    AverageCoroutineManager = SampleBufferCoroutineManager.Average(i => i);
                }
                if (SampleBufferGameScreen.Count > 0)
                {
                    AverageGameScreen = SampleBufferGameScreen.Average(i => i);
                }
                if (SampleBufferGameSessionUpdate.Count > 0)
                {
                    AverageGameSessionUpdate = SampleBufferGameSessionUpdate.Average(i => i);
                }
                if (SampleBufferParticleManager.Count > 0)
                {
                    AverageParticleManager = SampleBufferParticleManager.Average(i => i);
                }
                if (SampleBufferLightManager.Count > 0)
                {
                    AverageLightManager = SampleBufferLightManager.Average(i => i);
                }
                if (SampleBufferLevelUpdate.Count > 0)
                {
                    AverageLevelUpdate = SampleBufferLevelUpdate.Average(i => i);
                }
                if (SampleBufferCharacterUpdate.Count > 0)
                {
                    AverageCharacterUpdate = SampleBufferCharacterUpdate.Average(i => i);
                }
                if (SampleBufferStatusEffect.Count > 0)
                {
                    AverageStatusEffect = SampleBufferStatusEffect.Average(i => i);
                }
                if (SampleBufferSetTransforms.Count > 0)
                {
                    AverageSetTransforms = SampleBufferSetTransforms.Average(i => i);
                }

                if (SampleBufferMapEntityUpdate.Count > 0)
                {
                    AverageMapEntityUpdate = SampleBufferMapEntityUpdate.Average(i => i);
                }
                if (SampleBufferCharacterAnimUpdate.Count > 0)
                {
                    AverageCharacterAnimUpdate = SampleBufferCharacterAnimUpdate.Average(i => i);
                }
                if (SampleBufferSubmarineUpdate.Count > 0)
                {
                    AverageSubmarineUpdate = SampleBufferSubmarineUpdate.Average(i => i);
                }
                if (SampleBufferRagdollUpdate.Count > 0)
                {
                    AverageRagdollUpdate = SampleBufferRagdollUpdate.Average(i => i);
                }
                if (SampleBufferPhysicsWorldStep.Count > 0)
                {
                    AveragePhysicsWorldStep = SampleBufferPhysicsWorldStep.Average(i => i);
                }


                UpdateTimer = 0f;
            }
        }

        #region StopWatch MainUpdateLoop
        public Stopwatch SWMainUpdateLoop;
        public Queue<Double> SampleBufferMainUpdateLoop;
        public double AverageMainUpdateLoop;
        public void RecordMainLoopUpdate()
        {
            SampleBufferMainUpdateLoop.Enqueue(SWMainUpdateLoop.ElapsedTicks);

            if (SampleBufferMainUpdateLoop.Count > MaximumSamples)
            {
                SampleBufferMainUpdateLoop.Dequeue();
            }
            SWMainUpdateLoop.Stop();
            SWMainUpdateLoop.Reset();
        }
        #endregion

        #region StopWatch GUIUpdate
        public Stopwatch SWGUIUpdate;
        public Queue<Double> SampleBufferGUIUpdate;
        public double AverageGUIUpdate;
        public void RecordGUIUpdate()
        {
            SampleBufferGUIUpdate.Enqueue(SWGUIUpdate.ElapsedTicks);

            if (SampleBufferGUIUpdate.Count > MaximumSamples)
            {
                SampleBufferGUIUpdate.Dequeue();
            }
            SWGUIUpdate.Stop();
            SWGUIUpdate.Reset();
        }
        #endregion

        #region StopWatch DebugConsole
        public Stopwatch SWDebugConsole;
        public Queue<Double> SampleBufferDebugConsole;
        public double AverageDebugConsole;
        public void RecordDebugConsole()
        {
            SampleBufferDebugConsole.Enqueue(SWDebugConsole.ElapsedTicks);

            if (SampleBufferDebugConsole.Count > MaximumSamples)
            {
                SampleBufferDebugConsole.Dequeue();
            }
            SWDebugConsole.Stop();
            SWDebugConsole.Reset();
        }
        #endregion

        #region StopWatch PlayerInput
        public Stopwatch SWPlayerInput;
        public Queue<Double> SampleBufferPlayerInput;
        public double AveragePlayerInput;
        public void RecordPlayerInput()
        {
            SampleBufferPlayerInput.Enqueue(SWPlayerInput.ElapsedTicks);

            if (SampleBufferPlayerInput.Count > MaximumSamples)
            {
                SampleBufferPlayerInput.Dequeue();
            }
            SWPlayerInput.Stop();
            SWPlayerInput.Reset();
        }
        #endregion

        #region StopWatch SoundPlayer
        public Stopwatch SWSoundPlayer;
        public Queue<Double> SampleBufferSoundPlayer;
        public double AverageSoundPlayer;
        public void RecordSoundPlayer()
        {
            SampleBufferSoundPlayer.Enqueue(SWSoundPlayer.ElapsedTicks);

            if (SampleBufferSoundPlayer.Count > MaximumSamples)
            {
                SampleBufferSoundPlayer.Dequeue();
            }
            SWSoundPlayer.Stop();
            SWSoundPlayer.Reset();
        }
        #endregion

        #region StopWatch NetworkMember
        public Stopwatch SWNetworkMember;
        public Queue<Double> SampleBufferNetworkMember;
        public double AverageNetworkMember;
        public void RecordNetworkMember()
        {
            SampleBufferNetworkMember.Enqueue(SWNetworkMember.ElapsedTicks);

            if (SampleBufferNetworkMember.Count > MaximumSamples)
            {
                SampleBufferNetworkMember.Dequeue();
            }
            SWNetworkMember.Stop();
            SWNetworkMember.Reset();
        }
        #endregion

        #region StopWatch CoroutineManager
        public Stopwatch SWCoroutineManager;
        public Queue<Double> SampleBufferCoroutineManager;
        public double AverageCoroutineManager;
        public void RecordCoroutineManager()
        {
            SampleBufferCoroutineManager.Enqueue(SWCoroutineManager.ElapsedTicks);

            if (SampleBufferCoroutineManager.Count > MaximumSamples)
            {
                SampleBufferCoroutineManager.Dequeue();
            }
            SWCoroutineManager.Stop();
            SWCoroutineManager.Reset();
        }
        #endregion

        #region StopWatch GameScreen
        public Stopwatch SWGameScreen;
        public Queue<Double> SampleBufferGameScreen;
        public double AverageGameScreen;
        public void RecordGameScreen()
        {
            SampleBufferGameScreen.Enqueue(SWGameScreen.ElapsedTicks);

            if (SampleBufferGameScreen.Count > MaximumSamples)
            {
                SampleBufferGameScreen.Dequeue();
            }
            SWGameScreen.Stop();
            SWGameScreen.Reset();
        }
        #endregion

        #region StopWatch GameSessionUpdate
        public Stopwatch SWGameSessionUpdate;
        public Queue<Double> SampleBufferGameSessionUpdate;
        public double AverageGameSessionUpdate;
        public void RecordGameSessionUpdate()
        {
            SampleBufferGameSessionUpdate.Enqueue(SWGameSessionUpdate.ElapsedTicks);

            if (SampleBufferGameSessionUpdate.Count > MaximumSamples)
            {
                SampleBufferGameSessionUpdate.Dequeue();
            }
            SWGameSessionUpdate.Stop();
            SWGameSessionUpdate.Reset();
        }
        #endregion

        #region StopWatch ParticleManager
        public Stopwatch SWParticleManager;
        public Queue<Double> SampleBufferParticleManager;
        public double AverageParticleManager;
        public void RecordParticleManager()
        {
            SampleBufferParticleManager.Enqueue(SWParticleManager.ElapsedTicks);

            if (SampleBufferParticleManager.Count > MaximumSamples)
            {
                SampleBufferParticleManager.Dequeue();
            }
            SWParticleManager.Stop();
            SWParticleManager.Reset();
        }
        #endregion

        #region StopWatch LightManager
        public Stopwatch SWLightManager;
        public Queue<Double> SampleBufferLightManager;
        public double AverageLightManager;
        public void RecordLightManager()
        {
            SampleBufferLightManager.Enqueue(SWLightManager.ElapsedTicks);

            if (SampleBufferLightManager.Count > MaximumSamples)
            {
                SampleBufferLightManager.Dequeue();
            }
            SWLightManager.Stop();
            SWLightManager.Reset();
        }
        #endregion

        #region StopWatch LevelUpdate
        public Stopwatch SWLevelUpdate;
        public Queue<Double> SampleBufferLevelUpdate;
        public double AverageLevelUpdate;
        public void RecordLevelUpdate()
        {
            SampleBufferLevelUpdate.Enqueue(SWLevelUpdate.ElapsedTicks);

            if (SampleBufferLevelUpdate.Count > MaximumSamples)
            {
                SampleBufferLevelUpdate.Dequeue();
            }
            SWLevelUpdate.Stop();
            SWLevelUpdate.Reset();
        }
        #endregion

        #region StopWatch Character Update
        public Stopwatch SWCharacterUpdate;
        public Queue<Double> SampleBufferCharacterUpdate;
        public double AverageCharacterUpdate;
        public void RecordCharacterUpdate()
        {
            SampleBufferCharacterUpdate.Enqueue(SWCharacterUpdate.ElapsedTicks);

            if (SampleBufferCharacterUpdate.Count > MaximumSamples)
            {
                SampleBufferCharacterUpdate.Dequeue();
            }
            SWCharacterUpdate.Stop();
            SWCharacterUpdate.Reset();
        }
        #endregion

        #region StopWatch StatusEffect
        public Stopwatch SWStatusEffect;
        public Queue<Double> SampleBufferStatusEffect;
        public double AverageStatusEffect;
        public void RecordStatusEffect()
        {
            SampleBufferStatusEffect.Enqueue(SWStatusEffect.ElapsedTicks);

            if (SampleBufferStatusEffect.Count > MaximumSamples)
            {
                SampleBufferStatusEffect.Dequeue();
            }
            SWStatusEffect.Stop();
            SWStatusEffect.Reset();
        }
        #endregion

        #region StopWatch SetTransforms
        public Stopwatch SWSetTransforms;
        public Queue<Double> SampleBufferSetTransforms;
        public double AverageSetTransforms;
        public void RecordSetTransforms()
        {
            SampleBufferSetTransforms.Enqueue(SWSetTransforms.ElapsedTicks);

            if (SampleBufferSetTransforms.Count > MaximumSamples)
            {
                SampleBufferSetTransforms.Dequeue();
            }
            SWSetTransforms.Stop();
            SWSetTransforms.Reset();
        }
        #endregion

        #region StopWatch MapEntityUpdate
        public Stopwatch SWMapEntityUpdate;
        public Queue<Double> SampleBufferMapEntityUpdate;
        public double AverageMapEntityUpdate;
        public void RecordMapEntityUpdate()
        {
            SampleBufferMapEntityUpdate.Enqueue(SWMapEntityUpdate.ElapsedTicks);

            if (SampleBufferMapEntityUpdate.Count > MaximumSamples)
            {
                SampleBufferMapEntityUpdate.Dequeue();
            }
            SWMapEntityUpdate.Stop();
            SWMapEntityUpdate.Reset();
        }
        #endregion

        #region StopWatch CharacterAnimUpdate
        public Stopwatch SWCharacterAnimUpdate;
        public Queue<Double> SampleBufferCharacterAnimUpdate;
        public double AverageCharacterAnimUpdate;
        public void RecordCharacterAnimUpdate()
        {
            SampleBufferCharacterAnimUpdate.Enqueue(SWCharacterAnimUpdate.ElapsedTicks);

            if (SampleBufferCharacterAnimUpdate.Count > MaximumSamples)
            {
                SampleBufferCharacterAnimUpdate.Dequeue();
            }
            SWCharacterAnimUpdate.Stop();
            SWCharacterAnimUpdate.Reset();
        }
        #endregion

        #region StopWatch SubmarineUpdate
        public Stopwatch SWSubmarineUpdate;
        public Queue<Double> SampleBufferSubmarineUpdate;
        public double AverageSubmarineUpdate;
        public void RecordSubmarineUpdate()
        {
            SampleBufferSubmarineUpdate.Enqueue(SWSubmarineUpdate.ElapsedTicks);

            if (SampleBufferSubmarineUpdate.Count > MaximumSamples)
            {
                SampleBufferSubmarineUpdate.Dequeue();
            }
            SWSubmarineUpdate.Stop();
            SWSubmarineUpdate.Reset();
        }
        #endregion

        #region StopWatch RagdollUpdate
        public Stopwatch SWRagdollUpdate;
        public Queue<Double> SampleBufferRagdollUpdate;
        public double AverageRagdollUpdate;
        public void RecordRagdollUpdate()
        {
            SampleBufferRagdollUpdate.Enqueue(SWRagdollUpdate.ElapsedTicks);

            if (SampleBufferRagdollUpdate.Count > MaximumSamples)
            {
                SampleBufferRagdollUpdate.Dequeue();
            }
            SWRagdollUpdate.Stop();
            SWRagdollUpdate.Reset();
        }
        #endregion

        #region StopWatch PhysicsWorldStep
        public Stopwatch SWPhysicsWorldStep;
        public Queue<Double> SampleBufferPhysicsWorldStep;
        public double AveragePhysicsWorldStep;
        public void RecordPhysicsWorldStep()
        {
            SampleBufferPhysicsWorldStep.Enqueue(SWPhysicsWorldStep.ElapsedTicks);

            if (SampleBufferPhysicsWorldStep.Count > MaximumSamples)
            {
                SampleBufferPhysicsWorldStep.Dequeue();
            }
            SWPhysicsWorldStep.Stop();
            SWPhysicsWorldStep.Reset();
        }
        #endregion

        //Calling this should prevent the initial time being stupidly huge and avoid recording those entirely - also stops crashes.
        public void InitTimers()
        {
            SampleBufferMainUpdateLoop = new Queue<Double>();
            SWMainUpdateLoop = new Stopwatch();
            SWMainUpdateLoop.Start();
            SWMainUpdateLoop.Stop();
            SWMainUpdateLoop.Reset();

            SampleBufferGUIUpdate = new Queue<Double>();
            SWGUIUpdate = new Stopwatch();
            SWGUIUpdate.Start();
            SWGUIUpdate.Stop();
            SWGUIUpdate.Reset();

            SampleBufferDebugConsole = new Queue<Double>();
            SWDebugConsole = new Stopwatch();
            SWDebugConsole.Start();
            SWDebugConsole.Stop();
            SWDebugConsole.Reset();

            SampleBufferPlayerInput = new Queue<Double>();
            SWPlayerInput = new Stopwatch();
            SWPlayerInput.Start();
            SWPlayerInput.Stop();
            SWPlayerInput.Reset();

            SampleBufferSoundPlayer = new Queue<Double>();
            SWSoundPlayer = new Stopwatch();
            SWSoundPlayer.Start();
            SWSoundPlayer.Stop();
            SWSoundPlayer.Reset();

            SampleBufferNetworkMember = new Queue<Double>();
            SWNetworkMember = new Stopwatch();
            SWNetworkMember.Start();
            SWNetworkMember.Stop();
            SWNetworkMember.Reset();

            SampleBufferCoroutineManager = new Queue<Double>();
            SWCoroutineManager = new Stopwatch();
            SWCoroutineManager.Start();
            SWCoroutineManager.Stop();
            SWCoroutineManager.Reset();

            SampleBufferGameScreen = new Queue<Double>();
            SWGameScreen = new Stopwatch();
            SWGameScreen.Start();
            SWGameScreen.Stop();
            SWGameScreen.Reset();

            SampleBufferGameSessionUpdate = new Queue<Double>();
            SWGameSessionUpdate = new Stopwatch();
            SWGameSessionUpdate.Start();
            SWGameSessionUpdate.Stop();
            SWGameSessionUpdate.Reset();

            SampleBufferParticleManager = new Queue<Double>();
            SWParticleManager = new Stopwatch();
            SWParticleManager.Start();
            SWParticleManager.Stop();
            SWParticleManager.Reset();

            SampleBufferLightManager = new Queue<Double>();
            SWLightManager = new Stopwatch();
            SWLightManager.Start();
            SWLightManager.Stop();
            SWLightManager.Reset();

            SampleBufferLevelUpdate = new Queue<Double>();
            SWLevelUpdate = new Stopwatch();
            SWLevelUpdate.Start();
            SWLevelUpdate.Stop();
            SWLevelUpdate.Reset();

            SampleBufferCharacterUpdate = new Queue<Double>();
            SWCharacterUpdate = new Stopwatch();
            SWCharacterUpdate.Start();
            SWCharacterUpdate.Stop();
            SWCharacterUpdate.Reset();

            SampleBufferStatusEffect = new Queue<Double>();
            SWStatusEffect = new Stopwatch();
            SWStatusEffect.Start();
            SWStatusEffect.Stop();
            SWStatusEffect.Reset();

            SampleBufferSetTransforms = new Queue<Double>();
            SWSetTransforms = new Stopwatch();
            SWSetTransforms.Start();
            SWSetTransforms.Stop();
            SWSetTransforms.Reset();

            SampleBufferMapEntityUpdate = new Queue<Double>();
            SWMapEntityUpdate = new Stopwatch();
            SWMapEntityUpdate.Start();
            SWMapEntityUpdate.Stop();
            SWMapEntityUpdate.Reset();

            SampleBufferCharacterAnimUpdate = new Queue<Double>();
            SWCharacterAnimUpdate = new Stopwatch();
            SWCharacterAnimUpdate.Start();
            SWCharacterAnimUpdate.Stop();
            SWCharacterAnimUpdate.Reset();

            SampleBufferSubmarineUpdate = new Queue<Double>();
            SWSubmarineUpdate = new Stopwatch();
            SWSubmarineUpdate.Start();
            SWSubmarineUpdate.Stop();
            SWSubmarineUpdate.Reset();

            SampleBufferRagdollUpdate = new Queue<Double>();
            SWRagdollUpdate = new Stopwatch();
            SWRagdollUpdate.Start();
            SWRagdollUpdate.Stop();
            SWRagdollUpdate.Reset();

            SampleBufferPhysicsWorldStep = new Queue<Double>();
            SWPhysicsWorldStep = new Stopwatch();
            SWPhysicsWorldStep.Start();
            SWPhysicsWorldStep.Stop();
            SWPhysicsWorldStep.Reset();
        }

        

        
    }
}
