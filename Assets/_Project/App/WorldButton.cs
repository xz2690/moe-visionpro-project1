using System;
using MR.XR;
using TMPro;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// A 3D button (slab + label + collider) driven by the unified pointer: works with
    /// gaze + pinch on visionOS, controllers / hands on Quest and the mouse in the editor.
    /// Tapping it (press + release without dragging) raises Clicked.
    /// </summary>
    [RequireComponent(typeof(PointerInteractable))]
    public class WorldButton : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private TextMeshPro label;
        [SerializeField] private Renderer background;
        [SerializeField] private Color normalColor = new(0.18f, 0.44f, 0.85f);
        [SerializeField] private Color pressedColor = new(0.10f, 0.30f, 0.65f);
        [SerializeField] private Color disabledColor = new(0.35f, 0.35f, 0.38f);

        public event Action Clicked;

        private PointerInteractable _pointer;
        private bool _interactable = true;
        private Color _appliedColor = Color.clear;

        public bool Interactable
        {
            get => _interactable;
            set { _interactable = value; Refresh(); }
        }

        public string Text
        {
            get => label != null ? label.text : "";
            set { if (label != null) label.text = value; }
        }

        private void Awake()
        {
            _pointer = GetComponent<PointerInteractable>();
            if (label == null) label = GetComponentInChildren<TextMeshPro>();
            if (background == null) background = GetComponent<Renderer>();
            _pointer.Pressed += _ => Refresh();
            _pointer.Released += _ => Refresh();
            _pointer.Tapped += OnTapped;
            Refresh();
        }

        private void OnTapped()
        {
            if (_interactable) Clicked?.Invoke();
        }

        private void Refresh()
        {
            if (background == null) return;
            var color = !_interactable ? disabledColor : _pointer.IsPressed ? pressedColor : normalColor;
            if (color == _appliedColor) return;
            _appliedColor = color;
            background.material.SetColor(BaseColorId, color);
        }
    }
}
