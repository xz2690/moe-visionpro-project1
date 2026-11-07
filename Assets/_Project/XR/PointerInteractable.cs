using System;
using UnityEngine;

namespace MR.XR
{
    /// <summary>
    /// Receives unified pointer gestures (see PointerInput) for the collider hierarchy
    /// it sits on. A press that travels less than tapMaxDistance is also a Tap.
    /// </summary>
    public class PointerInteractable : MonoBehaviour
    {
        [Tooltip("Pointer travel (metres) below which a press-release counts as a tap.")]
        [SerializeField] private float tapMaxDistance = 0.03f;

        public event Action<PointerFrame> Pressed;
        public event Action<PointerFrame> Dragged;
        public event Action<PointerFrame> Released;
        public event Action Tapped;

        public bool IsPressed { get; private set; }
        public PointerSource Source { get; private set; }
        public Vector3 PressPosition { get; private set; }
        public Vector3 CurrentPosition { get; private set; }
        /// <summary>World-space movement of the pointer since the press.</summary>
        public Vector3 DragDelta => CurrentPosition - PressPosition;

        private float _maxTravel;

        internal void HandlePressed(PointerFrame frame)
        {
            IsPressed = true;
            Source = frame.Source;
            PressPosition = CurrentPosition = frame.Position;
            _maxTravel = 0f;
            Pressed?.Invoke(frame);
        }

        internal void HandleDragged(PointerFrame frame)
        {
            if (!IsPressed) return;
            CurrentPosition = frame.Position;
            _maxTravel = Mathf.Max(_maxTravel, DragDelta.magnitude);
            Dragged?.Invoke(frame);
        }

        internal void HandleReleased(PointerFrame frame)
        {
            if (!IsPressed) return;
            CurrentPosition = frame.Position;
            IsPressed = false;
            Released?.Invoke(frame);
            if (_maxTravel <= tapMaxDistance) Tapped?.Invoke();
        }

        private void OnDisable()
        {
            IsPressed = false;
        }
    }
}
