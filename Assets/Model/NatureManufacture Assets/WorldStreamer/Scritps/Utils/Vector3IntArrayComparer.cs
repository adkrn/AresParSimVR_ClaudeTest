using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldStreamer2
{
    /// <summary>
    /// Vector3Int array comparer.
    /// </summary>
    public class Vector3IntArrayComparer : IEqualityComparer<Vector3Int>
    {
        /// <summary>
        /// Equals the specified x and y.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        public bool Equals(Vector3Int x, Vector3Int y)
        {


            if (x.x == y.x && x.y == y.y && x.z == y.z)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the hash code.
        /// </summary>
        /// <returns>The hash code.</returns>
        /// <param name="obj">Object.</param>
        public int GetHashCode(Vector3Int obj)
        {
            int result = 17;

            unchecked
            {
                result = result * 23 + obj.x;
                result = result * 23 + obj.y;
                result = result * 23 + obj.z;
            }

            return result;
        }
    }
}