using MR.Networking;
using Unity.Netcode;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// The lobby's "Enter Room" / "Leave Room" button. Enabled once the session is up
    /// and the RoomController has spawned; the whole session enters or leaves together.
    /// </summary>
    [RequireComponent(typeof(WorldButton))]
    public class EnterRoomButton : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        private WorldButton _button;
        private float _nextRefresh;

        private void Awake()
        {
            _button = GetComponent<WorldButton>();
        }

        private void OnEnable()
        {
            _button.Clicked += OnClicked;
            RoomController.RoomStateChanged += OnRoomStateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            _button.Clicked -= OnClicked;
            RoomController.RoomStateChanged -= OnRoomStateChanged;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void OnClicked()
        {
            var room = RoomController.Instance;
            if (room == null || !room.IsSpawned) return;
            room.RequestToggle();
        }

        private void OnRoomStateChanged(bool open) => Refresh();

        private void Refresh()
        {
            var room = RoomController.Instance;
            var netManager = NetworkManager.Singleton;
            bool ready = room != null && room.IsSpawned && netManager != null && netManager.IsConnectedClient;

            _button.Interactable = ready;
            _button.Text = !ready ? "Connecting..." : room.IsRoomOpen ? "Leave Room" : "Enter Room";
        }
    }
}
