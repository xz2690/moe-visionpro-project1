using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace MR.XR
{
    public enum HandPoseSource : byte
    {
        None,
        HandTracking,
        Controller,
        Simulated
    }

    /// <summary>
    /// The local, non-networked rig: reads head and hand poses from whatever the
    /// current device provides. NetworkRig copies from here every frame, so the
    /// networking code never needs to know whether it runs on Quest or Vision Pro.
    /// </summary>
    public class HardwareRig : MonoBehaviour
    {
        public static HardwareRig Local { get; private set; }

        [SerializeField] private Transform head;
        [Tooltip("Parent of the tracked camera (XR Origin's Camera Offset). Device poses are relative to it.")]
        [SerializeField] private Transform trackingSpace;
        [Tooltip("Place fake hands in front of the head when no XR device is active (editor / Multiplayer Play Mode).")]
        [SerializeField] private bool simulateHandsWithoutXR = true;

        private static readonly List<XRHandSubsystem> HandSubsystems = new();
        private XRHandSubsystem _handSubsystem;

        public Transform Head => head;
        public Transform TrackingSpace => trackingSpace;

        private void Awake()
        {
            if (Local != null && Local != this) { Destroy(gameObject); return; }
            Local = this;

            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (trackingSpace == null) trackingSpace = head != null && head.parent != null ? head.parent : transform;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        /// <summary>Returns the running XR Hands subsystem, or null (e.g. Quest using controllers, editor).</summary>
        public XRHandSubsystem HandSubsystem
        {
            get
            {
                if (_handSubsystem != null && _handSubsystem.running) return _handSubsystem;

                _handSubsystem = null;
                SubsystemManager.GetSubsystems(HandSubsystems);
                foreach (var subsystem in HandSubsystems)
                {
                    if (!subsystem.running) continue;
                    _handSubsystem = subsystem;
                    break;
                }
                return _handSubsystem;
            }
        }

        /// <summary>World-space wrist pose of one hand from the best available source.</summary>
        public HandPoseSource TryGetWristPose(Handedness handedness, out Pose worldPose)
        {
            var subsystem = HandSubsystem;
            if (subsystem != null)
            {
                var hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
                if (hand.isTracked)
                {
                    worldPose = ToWorld(hand.rootPose);
                    return HandPoseSource.HandTracking;
                }
            }

            var node = handedness == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                worldPose = ToWorld(new Pose(position, rotation));
                return HandPoseSource.Controller;
            }

            if (simulateHandsWithoutXR && !XRSettings.isDeviceActive && head != null)
            {
                float side = handedness == Handedness.Left ? -1f : 1f;
                worldPose = new Pose(head.TransformPoint(new Vector3(0.18f * side, -0.25f, 0.35f)), head.rotation);
                return HandPoseSource.Simulated;
            }

            worldPose = default;
            return HandPoseSource.None;
        }

        /// <summary>Converts a pose in tracking (session) space to world space.</summary>
        public Pose ToWorld(Pose trackingPose)
        {
            if (trackingSpace == null) return trackingPose;
            return new Pose(
                trackingSpace.TransformPoint(trackingPose.position),
                trackingSpace.rotation * trackingPose.rotation);
        }
    }
}
