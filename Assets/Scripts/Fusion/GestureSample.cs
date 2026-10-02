namespace MMI.Fusion
{
    /// <summary>A named gesture measurement, e.g. Kind "handDistance". The engine passes it through; handlers interpret it.</summary>
    public readonly struct GestureSample
    {
        public readonly string Kind;
        public readonly float Value;

        public GestureSample(string kind, float value)
        {
            Kind = kind;
            Value = value;
        }
    }
}
