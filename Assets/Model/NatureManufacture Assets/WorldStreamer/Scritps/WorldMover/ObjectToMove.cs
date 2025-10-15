using UnityEngine;
using System.Collections;


namespace WorldStreamer2
{
    /// <summary>
    /// Object to move by world mover.
    /// </summary>
    public class ObjectToMove : MonoBehaviour
    {
        /// <summary>
        /// The world mover.
        /// </summary>
        //	public WorldMover worldMover;

        void Start()
        {
            GameObject worldMover = GameObject.FindGameObjectWithTag(WorldMover.WORLDMOVERTAG);
            if (worldMover != null)
                worldMover.GetComponent<WorldMover>().AddObjectToMove(transform);

        }
    }
}