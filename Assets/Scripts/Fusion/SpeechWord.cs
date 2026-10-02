namespace MMI.Fusion
{
    /// <summary>One recognized word, independent of the speech recognizer.</summary>
    public readonly struct SpeechWord
    {
        public readonly string Text;
        public readonly float Confidence;

        public SpeechWord(string text, float confidence = 1f)
        {
            Text = text;
            Confidence = confidence;
        }
    }
}
