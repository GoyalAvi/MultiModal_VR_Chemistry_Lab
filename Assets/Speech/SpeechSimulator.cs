using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace hci.mmi.speech.SpeechRecognitionSystem
{

    public class SpeechSimulator : MonoBehaviour
    {

        public string simulateSpeech;

        public delegate void SimulateSpeechInput(string input);
        public SimulateSpeechInput OnSimulatedSpeechInput;

        // Start is called before the first frame update
        void Start()
        { 

        }

        // Update is called once per frame
        void Update()
        {
            if(simulateSpeech.Contains(" "))
            {
                OnSimulatedSpeechInput?.Invoke(simulateSpeech.Replace(" ", ""));
                simulateSpeech = "";
            }
        }
    }
}

