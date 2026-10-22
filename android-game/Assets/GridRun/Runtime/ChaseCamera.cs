using UnityEngine;

namespace GridRun
{
    // Default chase view, matching the browser game's cameraPose() "chase" mode in dist/game.js:
    // behind and slightly right of the rider, aimed ahead, swinging smoothly through turns.
    public static class ChaseCamera
    {
        private const float Distance = 14, Height = 10.2f, Side = 5, Lead = 5, AimHeight = .55f, Sharpness = 6;

        public static void Target(Vector3 riderPosition, Quaternion riderRotation, out Vector3 position, out Vector3 aim)
        {
            Vector3 forward = riderRotation * Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            var right = new Vector3(forward.z, 0, -forward.x);
            var ground = new Vector3(riderPosition.x, 0, riderPosition.z);
            position = ground - forward * Distance + right * Side + Vector3.up * Height;
            aim = ground + forward * Lead + Vector3.up * AimHeight;
        }

        public static void Follow(Transform camera, ref Vector3 currentAim, Vector3 position, Vector3 aim, float deltaTime)
        {
            float blend = 1 - Mathf.Exp(-Sharpness * deltaTime);
            camera.position = Vector3.Lerp(camera.position, position, blend);
            currentAim = Vector3.Lerp(currentAim, aim, blend);
            camera.LookAt(currentAim);
        }

        public static void Snap(Transform camera, ref Vector3 currentAim, Vector3 position, Vector3 aim)
        {
            camera.position = position;
            currentAim = aim;
            camera.LookAt(aim);
        }
    }
}
