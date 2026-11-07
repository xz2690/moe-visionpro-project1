using MR.Voice;
using MR.XR;
using UnityEngine;

namespace MR.App
{
    /// <summary>The 3D mic button: color and pulse show the voice phase; a tap toggles listening.</summary>
    [RequireComponent(typeof(PointerInteractable))]
    public class MicOrb : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer _renderer;
        private Vector3 _baseScale;
        private Color _appliedColor = Color.clear;

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _baseScale = transform.localScale;
            GetComponent<PointerInteractable>().Tapped += () => VoiceController.Instance?.Toggle();
        }

        private void Update()
        {
            var voice = VoiceController.Instance;
            if (voice == null || _renderer == null) return;

            Color c;
            float pulse = 0f;
            switch (voice.Phase)
            {
                case VoicePhase.Listening: c = new Color(1f, 0.33f, 0.33f); pulse = 0.12f; break;
                case VoicePhase.Processing: c = new Color(0.98f, 0.75f, 0.14f); pulse = 0.06f; break;
                case VoicePhase.Speaking: c = new Color(0.29f, 0.87f, 0.5f); pulse = 0.05f; break;
                case VoicePhase.Error: c = new Color(0.97f, 0.44f, 0.44f); break;
                case VoicePhase.Unavailable: c = new Color(0.45f, 0.45f, 0.5f); break;
                default: c = new Color(0.55f, 0.75f, 1f); break;
            }

            if (c != _appliedColor)
            {
                _appliedColor = c;
                _renderer.material.SetColor(BaseColorId, c); // per-instance material: PolySpatial-safe
            }
            transform.localScale = _baseScale * (1f + pulse * Mathf.Sin(Time.time * 6f));
        }
    }
}
