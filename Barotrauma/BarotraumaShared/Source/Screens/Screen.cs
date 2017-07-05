namespace Barotrauma
{
    partial class Screen
    {
        private static Screen selected;
        
        public static Screen Selected
        {
            get { return selected; }
        }

        public static void SelectNull()
        {
            selected = null;
        }

        public virtual void Deselect()
        {
        }

        public virtual void Select()
        {
            if (selected != null && selected != this)
            {
                selected.Deselect();
#if CLIENT
                GUIComponent.KeyboardDispatcher.Subscriber = null;
#endif
            }
            selected = this;
        }

        public virtual Camera Cam
        {
            get { return null; }
        }
        
        public virtual void Update(double deltaTime)
        {
        }
    }
}
