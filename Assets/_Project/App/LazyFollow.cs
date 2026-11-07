using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// Keeps a panel in the lower part of the user's view. It stays put while the head moves a
    /// little and glides back once the head turns more than angleThreshold or moves more than
    /// moveThreshold. Follows the main camera, so it is independent of the seat the XR Origin
    /// was moved to. Ported from VoiceCreator.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        [SerializeField] private float distance = 1.1f;
        [SerializeField] private float yawOffset = 0f;
        [SerializeField] private float drop = 0.25f;
        [SerializeField] private float angleThreshold = 35f;
        [SerializeField] private float moveThreshold = 0.5f;
        [SerializeField] private float smoothTime = 0.35f;

        private Vector3 _target;
        private Vector3 _velocity;
        private bool _moving;
        private bool _placed;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform;
            Vector3 desired = Desired(head);

            if (!_placed)
            {
                transform.position = _target = desired;
                _placed = true;
            }
            else if (!_moving)
            {
                Vector3 toPanel = transform.position - head.position;
                Vector3 toDesired = desired - head.position;
                toPanel.y = toDesired.y = 0f;
                float angle = Vector3.Angle(toPanel, toDesired);
                float dist = Mathf.Abs(toPanel.magnitude - toDesired.magnitude);
                if (angle > angleThreshold || dist > moveThreshold || Vector3.Distance(transform.position, desired) > distance)
                {
                    _target = desired;
                    _moving = true;
                }
            }
            else
            {
                _target = desired;
            }

            if (_moving)
            {
                transform.position = Vector3.SmoothDamp(transform.position, _target, ref _velocity, smoothTime);
                if (Vector3.Distance(transform.position, _target) < 0.01f) _moving = false;
            }

            // Always face the user, upright. The panel's front is its -Z side.
            Vector3 away = transform.position - head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private Vector3 Desired(Transform head)
        {
            Vector3 f = head.forward;
            f.y = 0f;
            f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
            Vector3 dir = Quaternion.Euler(0f, yawOffset, 0f) * f;
            return head.position + dir * distance + Vector3.down * drop;
        }
    }
}
