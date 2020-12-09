using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    class Event
    {        
        protected bool isFinished;

        protected readonly EventPrefab prefab;
        
        public EventPrefab Prefab => prefab;

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
        
        public Event(EventPrefab prefab)
        {
            this.prefab = prefab;
        }
        
        public virtual IEnumerable<ContentFile> GetFilesToPreload()
        {
            yield break;
        }

        public virtual void Init(bool affectSubImmediately)
        {
        }

        public virtual void Update(float deltaTime)
        {
        }

        public virtual void Finished()
        {
            isFinished = true;
        }
        
        public virtual bool CanAffectSubImmediately(Level level)
        {
            return true;
        }

        public virtual bool LevelMeetsRequirements()
        {
            return true;
        }
    }
}
