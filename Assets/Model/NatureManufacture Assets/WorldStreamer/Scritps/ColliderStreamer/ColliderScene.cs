using UnityEngine;
using System.Collections;

namespace WorldStreamer2
{
    /// <summary>
    /// Collider scene.
    /// </summary>
    public class ColliderScene : MonoBehaviour
    {
        /// <summary>
        /// The name of the scene.
        /// </summary>
        public string sceneName;

        /// <summary>
        /// Start this instance adds to world mover and searches for collider streamer prefab.
        /// </summary>
        void Start()
        {
            GameObject.FindGameObjectWithTag(ColliderStreamerManager.COLLIDERSTREAMERMANAGERTAG).GetComponent<ColliderStreamerManager>().AddColliderScene(this);

            //GameObject mover = GameObject.FindGameObjectWithTag (WorldMover.WORLDMOVERTAG);
            //if (mover)
            //	mover.GetComponent<WorldMover> ().AddObjectToMove (transform);
        }
    }
}