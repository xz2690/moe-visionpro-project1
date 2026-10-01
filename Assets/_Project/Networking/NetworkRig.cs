using System;
using System.Collections.Generic;
using MR.Auth;
using MR.Core;
using MR.XR;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace MR.Networking
{
    /// <summary>
    /// The player prefab. The owner copies head / wrist poses from the local
    /// HardwareRig each frame; owner-authoritative NetworkTransforms on the
    /// Head / LeftHand / RightHand children replicate them to everyone else.
    /// The root itself never moves.
    /// </summary>
    public class NetworkRig : NetworkBehaviour
    {
        private const byte LeftTrackedBit = 1 << 0;
        private const byte RightTrackedBit = 1 << 1;

        private static readonly List<NetworkRig> Rigs = new();
        public static IReadOnlyList<NetworkRig> All => Rigs;
        public static event Action<NetworkRig> RigSpawned;
        public static event Action<NetworkRig> RigDespawned;

        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private TextMeshPro nameLabel;
        [SerializeField] private Renderer[] tintedRenderers;
        [Tooltip("In MR you already see your own body, so the local avatar is hidden by default.")]
        [SerializeField] private bool hideLocalAvatar = true;

        private readonly NetworkVariable<FixedString64Bytes> _displayName = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<FixedString64Bytes> _playerId = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<DevicePlatform> _platform = new(
            DevicePlatform.Other, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<byte> _trackedHands = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public string DisplayName => _displayName.Value.ToString();
        public string PlayerId => _playerId.Value.ToString();
        public DevicePlatform Platform => _platform.Value;
        public Color Color => PlayerColors.ForPlayerId(PlayerId);
        public Transform Head => head;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                var auth = AuthManager.Instance;
                var name = new FixedString64Bytes();
                name.CopyFromTruncated(auth != null ? auth.DisplayName : "Player");
                var id = new FixedString64Bytes();
                id.CopyFromTruncated(auth != null ? auth.PlayerId : "");

                _displayName.Value = name;
                _playerId.Value = id;
                _platform.Value = PlatformInfo.Current;
            }

            _displayName.OnValueChanged += OnIdentityChanged;
            _playerId.OnValueChanged += OnIdentityChanged;
            _trackedHands.OnValueChanged += OnTrackedHandsChanged;

            gameObject.name = $"NetworkRig_{OwnerClientId}";
            ApplyIdentity();
            ApplyVisibility();

            Rigs.Add(this);
            RigSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _displayName.OnValueChanged -= OnIdentityChanged;
            _playerId.OnValueChanged -= OnIdentityChanged;
            _trackedHands.OnValueChanged -= OnTrackedHandsChanged;

            Rigs.Remove(this);
            RigDespawned?.Invoke(this);
        }

        public static NetworkRig FindByOwner(ulong clientId)
        {
            foreach (var rig in Rigs)
                if (rig.OwnerClientId == clientId) return rig;
            return null;
        }

        private void LateUpdate()
        {
            if (!IsSpawned) return;

            if (IsOwner) CopyFromHardwareRig();
            FaceLabelToViewer();
        }

        private void CopyFromHardwareRig()
        {
            var rig = HardwareRig.Local;
            if (rig == null || rig.Head == null) return;

            head.SetPositionAndRotation(rig.Head.position, rig.Head.rotation);

            byte tracked = 0;
            if (rig.TryGetWristPose(Handedness.Left, out var left) != HandPoseSource.None)
            {
                leftHand.SetPositionAndRotation(left.position, left.rotation);
                tracked |= LeftTrackedBit;
            }
            if (rig.TryGetWristPose(Handedness.Right, out var right) != HandPoseSource.None)
            {
                rightHand.SetPositionAndRotation(right.position, right.rotation);
                tracked |= RightTrackedBit;
            }

            if (_trackedHands.Value != tracked)
                _trackedHands.Value = tracked;
        }

        private void FaceLabelToViewer()
        {
            if (nameLabel == null || !nameLabel.enabled) return;
            var viewer = Camera.main;
            if (viewer == null) return;

            Vector3 away = nameLabel.transform.position - viewer.transform.position;
            if (away.sqrMagnitude > 0.0001f)
                nameLabel.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void OnIdentityChanged(FixedString64Bytes previous, FixedString64Bytes current) => ApplyIdentity();

        private void OnTrackedHandsChanged(byte previous, byte current) => ApplyVisibility();

        private void ApplyIdentity()
        {
            if (nameLabel != null)
                nameLabel.text = $"{DisplayName}\n<size=60%>{PlatformInfo.ShortName(Platform)}</size>";

            // Per-instance material instead of MaterialPropertyBlock: PolySpatial (visionOS)
            // does not reliably support property blocks.
            foreach (var r in tintedRenderers)
                if (r != null) r.material.SetColor(BaseColorId, Color);
        }

        private void ApplyVisibility()
        {
            bool showAvatar = !(IsOwner && hideLocalAvatar);
            SetRenderersEnabled(head, showAvatar);
            SetRenderersEnabled(leftHand, showAvatar && (_trackedHands.Value & LeftTrackedBit) != 0);
            SetRenderersEnabled(rightHand, showAvatar && (_trackedHands.Value & RightTrackedBit) != 0);
        }

        private static void SetRenderersEnabled(Transform root, bool enabled)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.enabled = enabled;
        }
    }
}
