using UnityEngine;
using System.Collections;
using System.Linq;

namespace WorldStreamer2
{
    /// <summary>
    /// Scene split manager, finds streamer and adds scene.
    /// </summary>
    public class SceneSplitManager : MonoBehaviour
    {
        /// <summary>
        /// The name of the scene.
        /// </summary>
        public string sceneName;


        /// <summary>
        /// The gizmos color.
        /// </summary>
        public Color
            color;

        /// <summary>
        /// The split position.
        /// </summary>    
        public Vector3 position;

        /// <summary>
        /// The size of split.
        /// </summary>
        [HideInInspector]
        public Vector3 size = new Vector3(10, 10, 10);

        /// <summary>
        /// World Streamer Position
        /// </summary>
        [HideInInspector]
        public Vector3Int wsPosition = new Vector3Int(10, 10, 10);


        void OnDrawGizmosSelected()
        {
            // Display the explosion radius when selected
            Gizmos.color = color;
            Gizmos.DrawWireCube(position + size * 0.5f, size);
        }
    }
}