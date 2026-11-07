using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MR.Networking
{
    /// <summary>
    /// Shared "are we in the room" switch for the whole session. Any player can ask to
    /// open or close it (RPC to the owner); the owner flips a NetworkVariable; every
    /// client then loads or unloads the Room scene additively on its own. The room is
    /// visuals only (no in-scene NetworkObjects), so no NetworkSceneManager is involved
    /// and Distributed Authority stays simple. The owner spawns the table objects the
    /// first time the room opens; they stay in the session afterwards and just hide
    /// while the room is closed.
    /// </summary>
    public class RoomController : NetworkBehaviour
    {
        public const string RoomSceneName = "Room";

        public static RoomController Instance { get; private set; }
        public static event Action<bool> RoomStateChanged;

        [SerializeField] private GameObject[] interactablePrefabs;
        [SerializeField] private Vector3[] spawnPositions;

        private readonly NetworkVariable<bool> _roomOpen = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private AsyncOperation _loadOp;
        private bool _loaded;

        public bool IsRoomOpen => _roomOpen.Value;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.Distributable, clearAndSet: true);

            _roomOpen.OnValueChanged += OnRoomOpenChanged;
            ApplyRoomState(_roomOpen.Value);
        }

        public override void OnNetworkDespawn()
        {
            _roomOpen.OnValueChanged -= OnRoomOpenChanged;
            if (_loaded) UnloadRoom();
            RoomStateChanged?.Invoke(false);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ── Public API ─────────────────────────────

        public void RequestToggle() => RequestSetRoomRpc(!_roomOpen.Value);

        public void RequestSetRoom(bool open) => RequestSetRoomRpc(open);

        [Rpc(SendTo.Owner)]
        private void RequestSetRoomRpc(bool open)
        {
            if (_roomOpen.Value == open) return;
            _roomOpen.Value = open;
            Debug.Log($"[Room] {(open ? "Opening" : "Closing")} the room (requested by a player)");
        }

        // ── State ──────────────────────────────────

        private void OnRoomOpenChanged(bool previous, bool current) => ApplyRoomState(current);

        private void ApplyRoomState(bool open)
        {
            if (open) LoadRoom();
            else UnloadRoom();

            if (open && IsOwner) SpawnInteractablesIfMissing();

            RoomStateChanged?.Invoke(open);
        }

        private void SpawnInteractablesIfMissing()
        {
            if (FindAnyObjectByType<NetworkInteractable>() != null) return;
            if (interactablePrefabs == null) return;

            for (int i = 0; i < interactablePrefabs.Length; i++)
            {
                var prefab = interactablePrefabs[i];
                if (prefab == null) continue;
                var position = spawnPositions != null && i < spawnPositions.Length
                    ? spawnPositions[i]
                    : new Vector3((i - (interactablePrefabs.Length - 1) * 0.5f) * 0.35f, 0.85f, 1.2f);
                NetworkObject.InstantiateAndSpawn(prefab, NetworkManager, NetworkManager.LocalClientId,
                    position: position, rotation: Quaternion.identity);
            }
            Debug.Log($"[Room] Spawned {interactablePrefabs.Length} table objects");
        }

        // ── Scene ──────────────────────────────────

        private void LoadRoom()
        {
            if (_loaded || _loadOp != null) return;

            var scene = SceneManager.GetSceneByName(RoomSceneName);
            if (scene.isLoaded) { _loaded = true; return; }

            _loadOp = SceneManager.LoadSceneAsync(RoomSceneName, LoadSceneMode.Additive);
            if (_loadOp == null)
            {
                Debug.LogError($"[Room] Scene '{RoomSceneName}' is not in Build Settings.");
                return;
            }
            _loadOp.completed += _ =>
            {
                _loaded = true;
                _loadOp = null;
                // Closed again while still loading.
                if (!_roomOpen.Value) UnloadRoom();
            };
        }

        private void UnloadRoom()
        {
            if (!_loaded) return;
            _loaded = false;
            var scene = SceneManager.GetSceneByName(RoomSceneName);
            if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }
}
