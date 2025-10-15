using UnityEngine;
using System.Collections;


namespace WorldStreamer2
{
    /// <summary>
    /// Physic culling system.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicCullingSystem : MonoBehaviour
    {
        /// <summary>
        /// The bounding distance - Max view distance is referred from camera to terrain center point.
        /// </summary>
        [Tooltip("Max view distance is referred from camera to terrain center point")]
        public float physicDistance = 10000;

        float sphereSize = 0.5f;

        new Rigidbody rigidbody;


        CullingGroup group;
        BoundingSphere[] spheres = new BoundingSphere[1000];

        Camera mainCamera;

        [HideInInspector] public Vector3 velocity;
        [HideInInspector] public Vector3 angularVelocity;

        public bool gizmo = true;

        /// <summary>
        /// Start this instance, generates culling group and finds terrain.
        /// </summary>
        void Start()
        {
            rigidbody = GetComponent<Rigidbody>();

            group = new CullingGroup();
            group.targetCamera = Camera.main;

            spheres[0] = new BoundingSphere(transform.position, sphereSize);


            group.SetBoundingSpheres(spheres);
            group.SetBoundingSphereCount(1);

            group.onStateChanged = StateChangedMethod;


            group.SetBoundingDistances(new float[] { physicDistance });

            mainCamera = Camera.main;
            group.SetDistanceReferencePoint(Camera.main.transform);


            Invoke("CheckVisibility", 0.1f);
        }

        void OnDrawGizmosSelected()
        {
            if (gizmo)
            {
                Gizmos.color = Color.red;

                Gizmos.DrawWireSphere(transform.position, physicDistance);
            }
        }

        /// <summary>
        /// Checks visibility by hand
        /// </summary>
        void CheckVisibility()
        {
            bool visible = false || group.GetDistance(0) == 0;


            if (!visible)
            {
                StartMovement();
            }
        }

        /// <summary>
        /// sets object possition, and checks camera change;
        /// </summary>
        public void Update()
        {
            if (mainCamera != Camera.main)
            {
                mainCamera = Camera.main;
            }

            group.SetDistanceReferencePoint(Camera.main.transform);

            spheres[0].position = transform.position;
        }

        /// <summary>
        /// Event on cilling group change
        /// </summary>
        /// <param name="evt">Evt.</param>
        private void StateChangedMethod(CullingGroupEvent evt)
        {
            bool visible = false || group.GetDistance(0) == 0;

            if (visible)
            {
                StopMovement();
            }
            else
            {
                StartMovement();
            }
        }

        /// <summary>
        /// Raises the disable event.
        /// </summary>
        void OnDisable()
        {
            if (group != null)
            {
                group.Dispose();
                group = null;
            }
        }


        void StopMovement()
        {
#if UNITY_6_0 || UNITY_6000
            velocity = rigidbody.linearVelocity;
#else
            velocity = rigidbody.velocity;
#endif
            angularVelocity = rigidbody.angularVelocity;
            rigidbody.isKinematic = false;
        }

        void StartMovement()
        {
            rigidbody.isKinematic = true;
#if UNITY_6_0 || UNITY_6000
                velocity = rigidbody.linearVelocity;
#else
            velocity = rigidbody.velocity;
#endif
            rigidbody.angularVelocity = angularVelocity;
        }
    }
}