using System;
using System.Collections.Generic;
using System.Linq;
using Recognissimo.Components;
using UnityEngine;
using UnityEngine.UI;

namespace hci.mmi.speech.SpeechRecognitionSystem
{
    public class SpeechRecognitionSystem : MonoBehaviour
    {

        [SerializeField]
        private SpeechRecognizer recognizer;

        [SerializeField]
        private SpeechSimulator simulator;

        [SerializeField]
        private Recognissimo.Components.MicrophoneSpeechSource speechSource;

        [SerializeField]
        private bool muteSpeechInput;

        private bool mute;

        public bool Mute
        {
            get
            {
                return mute;
            }
            set
            {
                mute = value;
                speechSource.IsPaused = value;
                muteSpeechInput = value;
            }
        }

        public event EventHandler<Word> OnRecognized;
        public event EventHandler<Word> OnHypothesized;

        private void OnEnable()
        {
            Debug.Log("Starting Speech Recognition System");
            recognizer.PartialResultReady.AddListener(OnPartialResult);
            recognizer.ResultReady.AddListener(OnResult);

            recognizer.InitializationFailed.AddListener(OnError);
            recognizer.RuntimeFailed.AddListener(OnError);

            simulator.OnSimulatedSpeechInput += DoSimulatedSpeechInput;
        }

        private void Update()
        {
            if (muteSpeechInput != Mute)
            {
                Mute = muteSpeechInput;
            }
        }

        private void DoSimulatedSpeechInput(string input)
        {
            OnRecognized?.Invoke(this, new Word { text = input, confidence = 1f, startTime = (Time.realtimeSinceStartup - 0.5f), endTime = Time.realtimeSinceStartup });
        }

        private void OnPartialResult(PartialResult result)
        {
            foreach (Recognissimo.Components.Word word in result.partial_result)
            {
                OnHypothesized?.Invoke(this, new Word { text = word.word, confidence = word.conf, startTime = word.start, endTime = word.end });
            }
        }

        private void OnResult(Result result)
        {
            foreach (Recognissimo.Components.Word word in result.result)
            {
                OnRecognized?.Invoke(this, new Word { text = word.word, confidence = word.conf, startTime = word.start, endTime = word.end });
            }
        }

        private void OnError(Recognissimo.SpeechProcessorException exception)
        {
           
        }

    }
    public class Word : EventArgs
    {
        public string text;
        public float confidence;
        public float startTime;
        public float endTime;
    }
}

