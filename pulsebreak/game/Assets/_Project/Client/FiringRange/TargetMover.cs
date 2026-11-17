using UnityEngine;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>Slides a target back and forth along a line at constant speed, for tracking practice.</summary>
    public sealed class TargetMover : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        [Min(0f)] public float amplitude = 4f;
        [Min(0f)] public float speed = 3f;

        Vector3 _origin;
        float _travelled;

        void Start()
        {
            _origin = transform.localPosition;
            _travelled = amplitude; // start at the centre of the run
        }

        void Update()
        {
            if (amplitude <= 0f) return;
            _travelled += speed * Time.deltaTime;
            float offset = Mathf.PingPong(_travelled, amplitude * 2f) - amplitude;
            transform.localPosition = _origin + axis.normalized * offset;
        }
    }
}
