using MR.XR;
using Unity.Netcode;
using UnityEngine;

namespace MR.Networking
{
    /// <summary>
    /// Gives every player their own spot in the shared space: seats on a circle
    /// around the shared content, all facing the middle, so everyone sees the
    /// others and the cube without walking around.
    ///
    /// The seat comes from the Netcode client id, so it needs no extra sync and
    /// every client agrees on it. Moving a player means moving their XR Origin;
    /// anything that must stay put relative to the player (the PolySpatial Volume
    /// Camera on visionOS, the local status panel) is listed in carryAlong and
    /// moved by the same rigid transform. The devices don't share a physical room,
    /// so shifting the origin only changes where the shared content sits around
    /// each user.
    /// </summary>
    public class SeatAssigner : MonoBehaviour
    {
        // Opposite seats first, then the gaps, so small groups face each other.
        private static readonly float[] SeatAngles = { 0f, 180f, 90f, 270f, 45f, 225f, 135f, 315f };

        [Tooltip("Floor point the seats circle around (below the probe's orbit center).")]
        [SerializeField] private Vector3 center = new(0f, 0f, 1.2f);
        [SerializeField] private float radius = 1.2f;
        [Tooltip("The local XR Origin. Defaults to the HardwareRig's transform.")]
        [SerializeField] private Transform origin;
        [Tooltip("Moved together with the origin, keeping their pose relative to it.")]
        [SerializeField] private Transform[] carryAlong;

        private NetworkManager _netManager;
        private Pose _homePose;
        private Pose[] _relativePoses;

        public int CurrentSeat { get; private set; } = -1;

        private void Start()
        {
            if (origin == null && HardwareRig.Local != null) origin = HardwareRig.Local.transform;
            if (origin == null)
            {
                Debug.LogError("[Seat] No XR Origin to move.");
                enabled = false;
                return;
            }

            _homePose = new Pose(origin.position, origin.rotation);
            carryAlong ??= new Transform[0];
            _relativePoses = new Pose[carryAlong.Length];
            Quaternion inverseHome = Quaternion.Inverse(_homePose.rotation);
            for (int i = 0; i < carryAlong.Length; i++)
            {
                if (carryAlong[i] == null) continue;
                _relativePoses[i] = new Pose(
                    inverseHome * (carryAlong[i].position - _homePose.position),
                    inverseHome * carryAlong[i].rotation);
            }

            _netManager = NetworkManager.Singleton;
            if (_netManager != null)
            {
                _netManager.OnClientConnectedCallback += OnClientConnected;
                _netManager.OnClientStopped += OnClientStopped;
            }
        }

        private void OnDestroy()
        {
            if (_netManager == null) return;
            _netManager.OnClientConnectedCallback -= OnClientConnected;
            _netManager.OnClientStopped -= OnClientStopped;
        }

        /// <summary>Seat index for a client: Distributed Authority ids start at 1, so client 1 gets seat 0.</summary>
        public static int SeatFor(ulong clientId) =>
            (int)((clientId + (ulong)SeatAngles.Length - 1) % (ulong)SeatAngles.Length);

        /// <summary>World pose of a seat: on the circle, facing the center.</summary>
        public Pose SeatPose(int seat)
        {
            float rad = SeatAngles[seat % SeatAngles.Length] * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad)); // seat 0 is at -Z, i.e. the original spawn
            return new Pose(center + outward * radius, Quaternion.LookRotation(-outward, Vector3.up));
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId != _netManager.LocalClientId) return;
            CurrentSeat = SeatFor(clientId);
            MoveTo(SeatPose(CurrentSeat));
            Debug.Log($"[Seat] Client {clientId} -> seat {CurrentSeat} ({SeatAngles[CurrentSeat]:0}°)");
        }

        private void OnClientStopped(bool wasHost)
        {
            CurrentSeat = -1;
            MoveTo(_homePose);
        }

        private void MoveTo(Pose target)
        {
            origin.SetPositionAndRotation(target.position, target.rotation);
            for (int i = 0; i < carryAlong.Length; i++)
            {
                if (carryAlong[i] == null) continue;
                carryAlong[i].SetPositionAndRotation(
                    target.position + target.rotation * _relativePoses[i].position,
                    target.rotation * _relativePoses[i].rotation);
            }
        }
    }
}
