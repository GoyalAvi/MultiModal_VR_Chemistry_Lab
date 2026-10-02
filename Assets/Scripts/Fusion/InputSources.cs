using System;

namespace MMI.Fusion
{
    /// <summary>Speech input. Implement it on top of whatever recognizer the app uses.</summary>
    public interface ISpeechInputSource
    {
        event Action<SpeechWord> WordRecognized;
    }

    /// <summary>Pointing input. Implement it on top of the app's ray or gesture system.</summary>
    public interface IPointingInputSource
    {
        event Action<PointingSample> PointingChanged;
    }

    /// <summary>Non-pointing gesture input, e.g. the hand distance for "make it this big".</summary>
    public interface IGestureInputSource
    {
        event Action<GestureSample> GestureChanged;
    }
}
