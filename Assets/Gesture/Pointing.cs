using UnityEngine;

using System;

namespace hci.mmi.gesture.GestureRecognitionSystem
{
    public class Pointing: MonoBehaviour
    {

        [SerializeField]
        private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor;
        // Start is called before the first frame update

        public event EventHandler<Tuple<GameObject, Vector3>> OnPointingDirectionChanged;

        void Start()
        {
            
        }

        // Update is called once per frame
        void Update()
        {
            if (rayInteractor != null && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
            OnPointingDirectionChanged?.Invoke(this, new Tuple<GameObject, Vector3>(hit.collider.gameObject, hit.point));
            }
        }
    }
}
