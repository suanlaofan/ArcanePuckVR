using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcanePuck
{
    public sealed class DesktopCameraController : MonoBehaviour
    {
        private const float MoveSpeed = 3.5f;
        private const float LookSensitivity = 0.12f;
        private const float MinimumPitch = -80f;
        private const float MaximumPitch = 80f;

        private float fixedHeight;
        private float yaw;
        private float pitch;
        private bool initialized;

        public void Initialize()
        {
            fixedHeight = transform.position.y;
            SyncLookAngles();
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                Initialize();
            }

            HandleLook();
            HandleMovement();
        }

        private void HandleLook()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                SyncLookAngles();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * LookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * LookSensitivity, MinimumPitch, MaximumPitch);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            if (mouse.rightButton.wasReleasedThisFrame)
            {
                ReleaseCursor();
            }
        }

        private void HandleMovement()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            var input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (input.sqrMagnitude <= 0f)
            {
                return;
            }

            var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            var movement = (forward * input.y + right * input.x).normalized;
            var position = transform.position + movement * (MoveSpeed * Time.unscaledDeltaTime);
            position.y = fixedHeight;
            transform.position = position;
        }

        private void SyncLookAngles()
        {
            var angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        }

        private void OnDisable()
        {
            ReleaseCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ReleaseCursor();
            }
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
