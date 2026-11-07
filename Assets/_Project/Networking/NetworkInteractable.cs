using MR.XR;
using Unity.Netcode;
using UnityEngine;

namespace MR.Networking
{
    /// <summary>
    /// A shared object on the room table. Tap it to cycle its color, drag it to move it.
    /// Whoever touches it takes ownership (Distributed Authority, Transferable), so the
    /// owner-authoritative NetworkTransform and the color NetworkVariable follow the
    /// last person who interacted. Hidden locally while the room is closed.
    /// </summary>
    [RequireComponent(typeof(PointerInteractable))]
    public class NetworkInteractable : NetworkBehaviour
    {
        private const float OwnershipTimeout = 2f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer tintedRenderer;
        [SerializeField] private bool allowDrag = true;
        [SerializeField] private Color[] palette =
        {
            new(0.95f, 0.55f, 0.15f),
            new(0.25f, 0.55f, 0.95f),
            new(0.30f, 0.80f, 0.40f),
            new(0.90f, 0.30f, 0.35f),
            new(0.75f, 0.35f, 0.90f),
            new(0.95f, 0.85f, 0.25f)
        };

        private readonly NetworkVariable<int> _colorIndex = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PointerInteractable _pointer;
        private Collider[] _colliders;
        private Vector3 _baseScale;
        private Vector3 _dragStart;
        private bool _dragging;
        private bool _pendingTap;
        private float _pendingSince;
        private float _bounce;
        private Color _appliedColor = Color.clear;

        public int ColorIndex => _colorIndex.Value;

        private void Awake()
        {
            _pointer = GetComponent<PointerInteractable>();
            _colliders = GetComponentsInChildren<Collider>(true);
            if (tintedRenderer == null) tintedRenderer = GetComponentInChildren<Renderer>();
            _baseScale = transform.localScale;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                NetworkObject.SetOwnershipStatus(
                    NetworkObject.OwnershipStatus.Distributable | NetworkObject.OwnershipStatus.Transferable,
                    clearAndSet: true);
            }

            _colorIndex.OnValueChanged += OnColorChanged;
            _pointer.Pressed += OnPressed;
            _pointer.Dragged += OnDragged;
            _pointer.Released += OnReleased;
            _pointer.Tapped += OnTapped;
            RoomController.RoomStateChanged += SetVisible;

            ApplyColor();
            SetVisible(RoomController.Instance != null && RoomController.Instance.IsRoomOpen);
        }

        public override void OnNetworkDespawn()
        {
            _colorIndex.OnValueChanged -= OnColorChanged;
            _pointer.Pressed -= OnPressed;
            _pointer.Dragged -= OnDragged;
            _pointer.Released -= OnReleased;
            _pointer.Tapped -= OnTapped;
            RoomController.RoomStateChanged -= SetVisible;
        }

        public override void OnGainedOwnership()
        {
            if (!_pendingTap) return;
            _pendingTap = false;
            CycleColor();
        }

        // ── Pointer ────────────────────────────────

        private void OnPressed(PointerFrame frame)
        {
            if (!IsSpawned) return;
            _dragStart = transform.position;
            _dragging = allowDrag;
            TakeOwnership();
        }

        private void OnDragged(PointerFrame frame)
        {
            // Non-owners must not move it: the owner's NetworkTransform would snap it back.
            if (!_dragging || !IsOwner) return;
            transform.position = _dragStart + _pointer.DragDelta;
        }

        private void OnReleased(PointerFrame frame) => _dragging = false;

        private void OnTapped()
        {
            if (IsOwner)
            {
                CycleColor();
                return;
            }
            // Ownership transfer is in flight; apply the tap when it arrives.
            _pendingTap = true;
            _pendingSince = Time.time;
        }

        private void TakeOwnership()
        {
            if (IsOwner || !IsSpawned) return;
            if (NetworkObject.IsOwnershipLocked || NetworkObject.IsRequestInProgress || !NetworkObject.IsOwnershipTransferable)
            {
                Debug.Log($"[Interactable] {name}: cannot take ownership right now.");
                return;
            }
            NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);
        }

        // ── State ──────────────────────────────────

        private void CycleColor() => _colorIndex.Value = (_colorIndex.Value + 1) % palette.Length;

        private void OnColorChanged(int previous, int current)
        {
            ApplyColor();
            _bounce = 1f;
        }

        private void ApplyColor()
        {
            if (tintedRenderer == null || palette.Length == 0) return;
            var color = palette[Mathf.Abs(_colorIndex.Value) % palette.Length];
            if (color == _appliedColor) return; // material edits are re-sent to RealityKit on visionOS
            _appliedColor = color;
            tintedRenderer.material.SetColor(BaseColorId, color);
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
            foreach (var c in _colliders) c.enabled = visible;
        }

        private void Update()
        {
            if (_pendingTap && Time.time - _pendingSince > OwnershipTimeout) _pendingTap = false;

            if (_bounce > 0f)
            {
                _bounce = Mathf.Max(0f, _bounce - Time.deltaTime * 4f);
                float s = 1f + 0.25f * Mathf.Sin(_bounce * Mathf.PI);
                transform.localScale = _baseScale * s;
            }
        }
    }
}
