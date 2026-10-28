using UnityEngine;

namespace UltrakillBridge.Guest
{
    /// <summary>Thin helpers over ULTRAKILL's player singletons (NewMovement, CameraController).</summary>
    internal static class V1
    {
        public static NewMovement Movement => MonoSingleton<NewMovement>.TryGetInstance(out var nm) ? nm : null;
        public static CameraController Camera => MonoSingleton<CameraController>.TryGetInstance(out var cc) ? cc : null;

        /// <summary>The player exists, is activated (out of the sandbox "pit-falling" state) and alive.</summary>
        public static bool Ready
        {
            get
            {
                var nm = Movement;
                return nm != null && nm.activated && !nm.dead && nm.cc != null;
            }
        }

        /// <summary>Bottom of the player capsule (works standing and sliding).</summary>
        public static Vector3 Feet(NewMovement nm)
        {
            Vector3 p = nm.transform.position;
            var col = nm.playerCollider != null ? nm.playerCollider : nm.GetComponent<CapsuleCollider>();
            if (col == null) return p + Vector3.down * 1.5f;
            return new Vector3(p.x, col.bounds.min.y, p.z);
        }

        /// <summary>Pivot position that puts the standing capsule's feet at <paramref name="feet"/>.</summary>
        public static Vector3 PivotForFeet(Vector3 feet) => feet + Vector3.up * 1.5f;

        public static bool Grounded(NewMovement nm) => nm.gc != null && nm.gc.onGround;

        private static Vector3 _lastPos;
        private static float _lastPosTime = -1f;
        private static Vector3 _measured;

        /// <summary>
        /// How fast V1 is really moving (units/s). Rocket rides zero V1's rigidbody velocity every FixedUpdate and move it
        /// with the rocket (Grenade.FixedUpdate), so this uses the rocket's velocity then, and otherwise the larger of the
        /// rigidbody velocity and the measured frame-to-frame motion. Call once per frame.
        /// </summary>
        public static Vector3 MotionVelocity(NewMovement nm)
        {
            Vector3 pos = nm.transform.position;
            float now = Time.time;
            float dt = now - _lastPosTime;
            if (_lastPosTime >= 0f && dt > 1e-4f && dt < 0.25f)
            {
                Vector3 v = (pos - _lastPos) / dt;
                // A recall or respawn moves V1 hundreds of units in one frame: not motion.
                _measured = v.sqrMagnitude < 250f * 250f ? Vector3.Lerp(_measured, v, 0.5f) : Vector3.zero;
            }
            else if (dt >= 0.25f) _measured = Vector3.zero;
            _lastPos = pos;
            _lastPosTime = now;

            var rocket = nm.ridingRocket;
            if (rocket != null && rocket.rb != null) return rocket.rb.velocity;
            Vector3 rb = nm.rb != null ? nm.rb.velocity : Vector3.zero;
            return _measured.sqrMagnitude > rb.sqrMagnitude ? _measured : rb;
        }

        /// <summary>
        /// Moves V1 with its feet at <paramref name="feet"/> facing <paramref name="yawDeg"/>, the way checkpoints do
        /// (CheckPoint.ResetRoom), without reloading anything. Revives V1 if it is dead.
        /// </summary>
        public static void Teleport(Vector3 feet, float yawDeg, bool revive)
        {
            var nm = Movement;
            if (nm == null) return;
            var cc = nm.cc;
            Vector3 pivot = PivotForFeet(feet) + Vector3.up * 0.05f;
            if (nm.sliding) nm.StopSlide();
            nm.transform.position = pivot;
            nm.rb.position = pivot;
            nm.rb.velocity = Vector3.zero;
            nm.rb.SetCustomGravityMode(useCustomGravity: false);
            cc.gravityRotation = Quaternion.identity;
            cc.gravityVec = Physics.gravity.normalized;
            cc.rotationOffset = Quaternion.identity;
            cc.transitionRotationZ = 0f;
            cc.transitionRotationZSmooth = 0f;
            cc.tiltRotationZ = 0f;
            cc.tiltRotationZSmooth = 0f;
            cc.ResetCamera(yawDeg + 0.01f);
            cc.ApplyRotations();
            if (nm.gc != null) nm.gc.heavyFall = false;
            var keep = nm.GetComponent<KeepInBounds>();
            if (keep != null) keep.ForceApproveNewPosition();
            if (revive && nm.dead)
            {
                cc.activated = true;
                if (!nm.enabled) nm.enabled = true;
                nm.Respawn();
                nm.GetHealth(0, silent: true);
                cc.StopShake();
                nm.ActivatePlayer();
            }
        }

        /// <summary>In-place respawn used instead of StatsManager.Restart's scene reload.</summary>
        public static void RespawnInPlace()
        {
            var nm = Movement;
            if (nm == null) return;
            Teleport(Feet(nm), nm.cc != null ? nm.cc.rotationY : 0f, revive: true);
        }
    }
}
