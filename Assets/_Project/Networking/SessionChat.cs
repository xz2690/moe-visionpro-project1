using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MR.Networking
{
    /// <summary>
    /// Session-wide text: what each player said (speech recognition subtitles) and short
    /// system lines. One instance per session, spawned by the session owner like the
    /// RoomController; any client may broadcast through it.
    /// </summary>
    public class SessionChat : NetworkBehaviour
    {
        public const int MaxTextBytes = 500;

        public static SessionChat Instance { get; private set; }

        /// <summary>(sender display name, text, sent by this client)</summary>
        public static event Action<string, string, bool> MessageReceived;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
                NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.Distributable, clearAndSet: true);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>Broadcasts a line from the local player to everyone in the session (including us).</summary>
        public void Say(string senderName, string text)
        {
            if (!IsSpawned || string.IsNullOrWhiteSpace(text)) return;
            var name = new FixedString64Bytes();
            name.CopyFromTruncated(senderName ?? "?");
            var body = new FixedString512Bytes();
            body.CopyFromTruncated(text.Length > MaxTextBytes ? text[..MaxTextBytes] : text);
            SayRpc(name, body);
        }

        [Rpc(SendTo.Everyone)]
        private void SayRpc(FixedString64Bytes senderName, FixedString512Bytes text, RpcParams rpcParams = default)
        {
            bool fromSelf = rpcParams.Receive.SenderClientId == NetworkManager.LocalClientId;
            MessageReceived?.Invoke(senderName.ToString(), text.ToString(), fromSelf);
        }
    }
}
