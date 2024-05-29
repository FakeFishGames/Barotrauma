using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class Event
    {
        public event Action Finished;
        protected bool isFinished;

        public readonly int RandomSeed;

        protected readonly EventPrefab prefab;
        
        public EventPrefab Prefab => prefab;

        public EventSet ParentSet { get; private set; }

        public bool Initialized { get; private set; }

        public Func<Level.InterestingPosition, bool> SpawnPosFilter;

        public bool IsFinished
        {
            get { return isFinished; }
        }
        
        public override string ToString()
        {
            return "Event (" + prefab.EventType.ToString() +")";
        }

        public virtual Vector2 DebugDrawPos
        {
            get
            {
                return Vector2.Zero;
            }
        }
        
        public Event(EventPrefab prefab, int seed)
        {
            RandomSeed = seed;
            this.prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
        }
        
        public virtual IEnumerable<ContentFile> GetFilesToPreload()
        {
            yield break;
        }

        public void Init(EventSet parentSet = null)
        {
            Initialized = true;
            ParentSet = parentSet;
            InitEventSpecific(parentSet);
        }

        protected virtual void InitEventSpecific(EventSet parentSet = null)
        {
        }

        public virtual string GetDebugInfo()
        {
            return $"Finished: {IsFinished.ColorizeObject()}";
        }

        public virtual void Update(float deltaTime)
        {
        }

        public virtual void Finish()
        {
            isFinished = true;
            Finished?.Invoke();
        } 

        public virtual bool LevelMeetsRequirements()
        {
            return true;
        }
    }
}
