using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace MR.XR
{
    /// <summary>
    /// Lets editor / Multiplayer Play Mode players move their "head" without a headset:
    /// WASD + Q/E to move, hold right mouse button to look. Disabled whenever an XR
    /// device is active, so it is harmless in device builds.
    /// </summary>
    public class EditorFlyCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float lookSpeed = 0.15f;

        private float _yaw;
        private float _pitch;

        private void OnEnable()
        {
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
        }

        private void Update()
        {
            if (XRSettings.isDeviceActive) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * lookSpeed;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSpeed, -80f, 80f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move += Vector3.down;

            if (move != Vector3.zero)
                transform.Translate(move.normalized * (moveSpeed * Time.deltaTime), Space.Self);
        }
    }
}
