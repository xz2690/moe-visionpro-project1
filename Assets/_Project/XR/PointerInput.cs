using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
#if UNITY_VISIONOS || UNITY_EDITOR
using Unity.PolySpatial.InputDevices;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace MR.XR
{
    public enum PointerPhase : byte { None, Began, Moved, Ended }

    public enum PointerSource : byte { None, SpatialPointer, Controller, Hand, Mouse }

    /// <summary>One frame of the unified pointer: "the user is pointing at X and pinching / pressing".</summary>
    public struct PointerFrame
    {
        public PointerPhase Phase;
        public PointerSource Source;
        /// <summary>World-space ray when the gesture began (gaze, controller or mouse).</summary>
        public Ray Ray;
        /// <summary>Object under the pointer when the gesture began; null when it began on nothing.</summary>
        public GameObject Target;
        /// <summary>World-space interaction point. Follows the hand / controller / mouse while held.</summary>
        public Vector3 Position;

        public bool IsHeld => Phase == PointerPhase.Began || Phase == PointerPhase.Moved;
    }

    /// <summary>
    /// Cross-platform "look at it and pinch" input, so interaction code never needs
    /// to know which device it runs on. Sources, first available wins:
    /// 1. visionOS gaze + pinch (PolySpatial SpatialPointerDevice; also mouse clicks in the visionOS simulator)
    /// 2. XR controllers: aim ray + trigger (Quest, Meta XR Simulator)
    /// 3. Tracked hands: Meta aim pose + index pinch (Quest hands)
    /// 4. Mouse: camera ray + left button (editor without a headset)
    /// Dispatches Pressed / Dragged / Released to the PointerInteractable under the pointer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PointerInput : MonoBehaviour
    {
        public static PointerInput Instance { get; private set; }

        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private float maxRayDistance = 10f;

        public PointerFrame Frame { get; private set; }
        public event System.Action<PointerFrame> FrameUpdated;

        private bool _held;
        private PointerSource _heldSource;
        private GameObject _heldTarget;
        private float _holdDistance;
        private PointerInteractable _current;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _injectActive;
        private Ray _injectedRay;
        private bool _injectedPressed;

        /// <summary>
        /// Test hook: drives the pointer from a world-space ray instead of a device, so
        /// interactions can be exercised from editor scripts and automated tests. The
        /// injected ray stays in effect until it is released and the gesture has ended.
        /// </summary>
        public void Inject(Ray ray, bool pressed)
        {
            _injectActive = true;
            _injectedRay = ray;
            _injectedPressed = pressed;
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var frame = new PointerFrame();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_injectActive)
            {
                bool injected = ProcessRay(ref frame, _injectedRay, _injectedPressed, PointerSource.Mouse);
                if (!_injectedPressed && !_held) _injectActive = false;
                Frame = injected ? frame : default;
                Dispatch(Frame);
                FrameUpdated?.Invoke(Frame);
                return;
            }
#endif
            bool any = TryReadSpatialPointer(ref frame)
                       || TryReadController(ref frame)
                       || TryReadHand(ref frame)
                       || TryReadMouse(ref frame);
            if (!any) frame = default;

            Frame = frame;
            Dispatch(frame);
            FrameUpdated?.Invoke(frame);
        }

        // ── Sources ────────────────────────────────

        private bool TryReadSpatialPointer(ref PointerFrame frame)
        {
#if UNITY_VISIONOS || UNITY_EDITOR
            var device = InputSystem.GetDevice<SpatialPointerDevice>();
            if (device == null) return false;

            var state = device.primaryInput.ReadValue();
            bool inProgress = state.phase == SpatialPointerPhase.Began || state.phase == SpatialPointerPhase.Moved;
            bool ended = state.phase == SpatialPointerPhase.Ended || state.phase == SpatialPointerPhase.Cancelled;

            if (inProgress && !_held)
            {
                _held = true;
                _heldSource = PointerSource.SpatialPointer;
                _heldTarget = state.targetObject;
                frame.Phase = PointerPhase.Began;
                frame.Source = _heldSource;
                frame.Target = _heldTarget;
                frame.Ray = new Ray(state.startInteractionRayOrigin, state.startInteractionRayDirection);
                frame.Position = state.interactionPosition;
                return true;
            }
            if (_heldSource != PointerSource.SpatialPointer || !_held) return false;

            frame.Source = _heldSource;
            frame.Target = _heldTarget;
            frame.Ray = new Ray(state.startInteractionRayOrigin, state.startInteractionRayDirection);
            frame.Position = state.interactionPosition;
            if (inProgress)
            {
                frame.Phase = PointerPhase.Moved;
                return true;
            }
            if (ended)
            {
                frame.Phase = PointerPhase.Ended;
                _held = false;
                _heldTarget = null;
                return true;
            }
            return false;
#else
            return false;
#endif
        }

        private bool TryReadController(ref PointerFrame frame)
        {
            if (!XRSettings.isDeviceActive) return false;
            var rig = HardwareRig.Local;
            if (rig == null) return false;

            foreach (var node in new[] { XRNode.RightHand, XRNode.LeftHand })
            {
                var device = InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid) continue;
                if (!device.TryGetFeatureValue(XRCommonUsages.devicePosition, out Vector3 position) ||
                    !device.TryGetFeatureValue(XRCommonUsages.deviceRotation, out Quaternion rotation))
                    continue;

                bool pressed = (device.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool trigger) && trigger) ||
                               (device.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool primary) && primary);

                // While a gesture is held, only the hand that started it may continue it.
                if (_held && _heldSource == PointerSource.Controller && !pressed && node == XRNode.RightHand)
                {
                    var other = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                    if (other.isValid && other.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool lt) && lt) continue;
                }

                var pose = rig.ToWorld(new Pose(position, rotation));
                var ray = new Ray(pose.position, pose.rotation * Vector3.forward);
                if (ProcessRay(ref frame, ray, pressed, PointerSource.Controller)) return true;
                if (_held && _heldSource == PointerSource.Controller) return false;
            }
            return false;
        }

        private bool TryReadHand(ref PointerFrame frame)
        {
            var rig = HardwareRig.Local;
            if (rig == null) return false;

            foreach (var hand in new[] { MetaAimHand.right, MetaAimHand.left })
            {
                if (hand == null || !hand.added || !hand.isTracked.isPressed) continue;

                var pose = rig.ToWorld(new Pose(hand.devicePosition.ReadValue(), hand.deviceRotation.ReadValue()));
                var ray = new Ray(pose.position, pose.rotation * Vector3.forward);
                if (ProcessRay(ref frame, ray, hand.indexPressed.isPressed, PointerSource.Hand)) return true;
            }
            return false;
        }

        private bool TryReadMouse(ref PointerFrame frame)
        {
            if (XRSettings.isDeviceActive) return false;
            var mouse = Mouse.current;
            var camera = Camera.main;
            if (mouse == null || camera == null) return false;

            // Right button is the editor fly camera's look control.
            bool pressed = mouse.leftButton.isPressed && !mouse.rightButton.isPressed;
            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            return ProcessRay(ref frame, ray, pressed, PointerSource.Mouse);
        }

        /// <summary>Turns a ray + button state into Began / Moved / Ended, keeping the hit distance while held.</summary>
        private bool ProcessRay(ref PointerFrame frame, Ray ray, bool pressed, PointerSource source)
        {
            if (_held && _heldSource != source) return false;

            if (pressed && !_held)
            {
                _held = true;
                _heldSource = source;
                if (TryPick(ray, out var hit))
                {
                    _heldTarget = hit.collider.gameObject;
                    _holdDistance = hit.distance;
                }
                else
                {
                    _heldTarget = null;
                    _holdDistance = 1f;
                }
                frame.Phase = PointerPhase.Began;
            }
            else if (pressed)
            {
                frame.Phase = PointerPhase.Moved;
            }
            else if (_held)
            {
                frame.Phase = PointerPhase.Ended;
                _held = false;
            }
            else
            {
                return false;
            }

            frame.Source = source;
            frame.Target = _heldTarget;
            frame.Ray = ray;
            frame.Position = ray.GetPoint(_holdDistance);
            if (frame.Phase == PointerPhase.Ended) _heldTarget = null;
            return true;
        }

        /// <summary>
        /// Nearest hit that belongs to a PointerInteractable, else the nearest hit. PolySpatial
        /// (editor and visionOS) keeps mirror objects with copies of our colliders at the same
        /// place; they carry none of our components, so a plain Raycast could land on a mirror.
        /// </summary>
        private bool TryPick(Ray ray, out RaycastHit picked)
        {
            var hits = Physics.RaycastAll(ray, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                picked = default;
                return false;
            }
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<PointerInteractable>() == null) continue;
                picked = hit;
                return true;
            }
            picked = hits[0];
            return true;
        }

        // ── Dispatch ───────────────────────────────

        private void Dispatch(PointerFrame frame)
        {
            switch (frame.Phase)
            {
                case PointerPhase.Began:
                    _current = frame.Target != null ? frame.Target.GetComponentInParent<PointerInteractable>() : null;
                    if (_current != null) _current.HandlePressed(frame);
                    break;
                case PointerPhase.Moved:
                    if (_current != null) _current.HandleDragged(frame);
                    break;
                case PointerPhase.Ended:
                    if (_current != null) _current.HandleReleased(frame);
                    _current = null;
                    break;
            }
        }
    }
}
