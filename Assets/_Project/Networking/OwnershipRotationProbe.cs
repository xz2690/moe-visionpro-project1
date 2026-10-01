using System.Collections.Generic;
using MR.Core;
using Unity.Netcode;
using UnityEngine;

namespace MR.Networking
{
    /// <summary>
    /// Phase 0 test object. Needs no input, so it can be judged by eye on every device:
    /// the owner flies the cube in a circle, and every few seconds hands ownership
    /// to the next connected client. The cube takes the owner's avatar color.
    /// A smooth orbit that changes color without jumping means spawn, transform
    /// sync and ownership transfer all work across the platforms in the session.
    /// </summary>
    public class OwnershipRotationProbe : NetworkBehaviour
    {
        [SerializeField] private float secondsPerOwner = 5f;
        [SerializeField] private Vector3 orbitCenter = new(0f, 1.2f, 1.2f);
        [SerializeField] private float orbitRadius = 0.4f;
        [SerializeField] private float degreesPerSecond = 45f;

        private readonly List<ulong> _clientIds = new();
        private Renderer _renderer;
        private Color _appliedColor = Color.clear;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private float _ownedSince;
        private float _angle;

        public int Handoffs { get; private set; }

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                NetworkObject.SetOwnershipStatus(
                    NetworkObject.OwnershipStatus.Distributable | NetworkObject.OwnershipStatus.Transferable,
                    clearAndSet: true);
                BeginOwnership();
            }
            ApplyOwnerColor();
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            Handoffs++;
            if (IsOwner) BeginOwnership();
            ApplyOwnerColor();
            Debug.Log($"[Probe] Ownership {previous} -> {current} (local {NetworkManager.LocalClientId})");
        }

        private void BeginOwnership()
        {
            _ownedSince = Time.time;
            // Continue the orbit from wherever the previous owner left the cube.
            Vector3 offset = transform.position - orbitCenter;
            _angle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // Remote rigs may spawn after the cube, so keep the color current.
            if (Time.frameCount % 30 == 0) ApplyOwnerColor();

            if (!IsOwner) return;

            _angle += degreesPerSecond * Time.deltaTime;
            float rad = _angle * Mathf.Deg2Rad;
            transform.SetPositionAndRotation(
                orbitCenter + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius,
                Quaternion.Euler(0f, -_angle, 0f));

            if (Time.time - _ownedSince >= secondsPerOwner)
                HandOffToNextClient();
        }

        private void HandOffToNextClient()
        {
            _clientIds.Clear();
            _clientIds.AddRange(NetworkManager.ConnectedClientsIds);
            _clientIds.Sort();

            if (_clientIds.Count < 2)
            {
                _ownedSince = Time.time;
                return;
            }

            int index = _clientIds.IndexOf(OwnerClientId);
            ulong next = _clientIds[(index + 1) % _clientIds.Count];
            _ownedSince = Time.time; // avoid re-sending while the change is in flight
            NetworkObject.ChangeOwnership(next);
        }

        private void ApplyOwnerColor()
        {
            if (_renderer == null) return;
            var ownerRig = NetworkRig.FindByOwner(OwnerClientId);
            Color color = ownerRig != null ? ownerRig.Color : PlayerColors.ForClientId(OwnerClientId);
            if (color == _appliedColor) return; // material edits are re-sent to RealityKit on visionOS
            _appliedColor = color;
            _renderer.material.SetColor(BaseColorId, color);
        }
    }
}
