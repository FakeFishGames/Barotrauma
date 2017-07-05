namespace Barotrauma.Networking
{
    abstract partial class NetworkMember
    {
        protected const CharacterInfo characterInfo = null;

        protected const Character myCharacter = null;

        public CharacterInfo CharacterInfo
        {
            get { return null; }
        }

        public Character Character
        {
            get { return null; }
        }

        private void InitProjSpecific()
        {
            //do nothing
        }
    }
}
