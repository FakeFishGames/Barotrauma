namespace Barotrauma
{
    abstract class FishSwimAnimation : SwimAnimation
    {
        protected FishSwimAnimation(string file) : base(file) { }

        [Serialize(1f, true), Editable]
        public float WaveAmplitude
        {
            get;
            set;
        }

        [Serialize(1f, true), Editable]
        public float WaveLength
        {
            get;
            set;
        }

        [Serialize(25.0f, true), Editable]
        public float SteerTorque
        {
            get;
            set;
        }
    }

    abstract class FishWalkAnimation : WalkAnimation
    {
        protected FishWalkAnimation(string file) : base(file) { }

        [Serialize(0.0f, true), Editable]
        public float LegTorque
        {
            get;
            set;
        }
    }

    #region Mantis
    class MantisSwimAnimation : FishSwimAnimation
    {
        protected override string CharacterName => @"Mantis";
        protected override string ClipName => @"MantisSwim.xml";
        public static MantisSwimAnimation Instance { get; private set; }

        protected MantisSwimAnimation(string file) : base(file)
        {
            Instance = new MantisSwimAnimation(Path);
        }
    }

    // TODO: Interface?
    class MantisWalkAnimation : FishWalkAnimation
    {
        protected override string CharacterName => @"Mantis";
        protected override string ClipName => @"MantisWalk.xml";
        public static MantisWalkAnimation Instance { get; private set; }

        protected MantisWalkAnimation(string file) : base(file)
        {
            Instance = new MantisWalkAnimation(Path);
        }
    }

    //class MantisRunAnimation: FishWalkAnimation
    //{
    //    public readonly static MantisRunAnimation Instance = new MantisRunAnimation($"{CHARACTERS_FOLDER}/Mantis/MantisRun.xml");
    //    protected MantisRunAnimation(string file) : base(file) { }
    //}
    #endregion
}
