// Crossing from the simulation's frame (right-handed: x east, y up, z south;
// cars face -z at yaw 0) into Unity's left-handed frame: z is negated.
using UnityEngine;

namespace Halcyon
{
    public static class Frame
    {
        public static Vector3 Pos(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        public static Vector3 Dir(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        public static Quaternion Rot(Core.Quat q) => new Quaternion((float)-q.x, (float)-q.y, (float)q.z, (float)q.w);

        /// <summary>Unity rotation for a heading in the simulation (yaw 0 faces -z there, +z here).</summary>
        public static Quaternion Yaw(double yaw) => Quaternion.Euler(0, (float)(-yaw * Mathf.Rad2Deg), 0);

        /// <summary>Simulation position from a Unity one.</summary>
        public static void ToSim(Vector3 p, out double x, out double y, out double z)
        {
            x = p.x;
            y = p.y;
            z = -p.z;
        }
    }
}
