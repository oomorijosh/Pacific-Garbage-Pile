using UnityEngine;
using UnityEngine.InputSystem;

namespace PacificExhibit
{
    /// <summary>Optional mouse controls in the standalone authoring scene.</summary>
    public sealed class PacificExhibitOrbit : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 2.3f, 0f);
        public float distance = 10.5f;
        public float yaw = 29f;
        public float pitch = 9f;
        float initialDistance, initialYaw, initialPitch;

        void Awake()
        {
            initialDistance = distance;
            initialYaw = yaw;
            initialPitch = pitch;
            ApplyPose();
        }

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * 0.18f;
                    pitch = Mathf.Clamp(pitch - delta.y * 0.14f, -4f, 55f);
                }
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.008f, 6f, 18f);
            }
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                distance = initialDistance;
                yaw = initialYaw;
                pitch = initialPitch;
            }
            ApplyPose();
        }

        public void ApplyPose()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = target - rotation * Vector3.forward * distance;
            // Keep the viewer inside the room even when zooming out or circling behind the box.
            position.x = Mathf.Clamp(position.x, -11.6f, 11.6f);
            position.y = Mathf.Clamp(position.y, 0.3f, 8.6f);
            position.z = Mathf.Clamp(position.z, -15.6f, 8.6f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
        }
    }
}
