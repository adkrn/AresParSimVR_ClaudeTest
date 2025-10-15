using System;
using UnityEngine;
using System.Collections;
using UnityEngine.Serialization;

#if UNITY_5_3 || UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

namespace WorldStreamer2
{
    /// <summary>
    /// Collider streamer.
    /// </summary>
    public class ColliderStreamer : MonoBehaviour
    {
        /// <summary>
        /// The name of the scene.
        /// </summary>
        [Tooltip("Scene name that belongs to this collider.")]
        public string sceneName;

        /// <summary>
        /// The scene path.
        /// </summary>
        [Tooltip("Path where collider streamer should find scene which has to loaded after collider hit.")]
        public string scenePath;

        /// <summary>
        /// The scene game object.
        /// </summary>
        public GameObject sceneGameObject;

        /// <summary>
        /// The collider streamer manager.
        /// </summary>
        [HideInInspector] public ColliderStreamerManager colliderStreamerManager;

        /// <summary>
        /// The player only activate.
        /// </summary>
        [Tooltip("If it's checkboxed only player could activate collider to start loading, otherwise every physical hit could activate it.")]
        public bool playerOnlyActivate = true;

        /// <summary>
        /// The unload timer.
        /// </summary>
        [Tooltip("Time in seconds after which scene will be unloaded when \"Player\" or object that activate loading will left collider area.")]
        public float unloadTimer = 0;

        public enum State
        {
            Start,
            Ready,
            Loading,
            Loaded,
            Unloading,
            Unloaded
        }

        public State currentState = State.Start;

        private float _unloadTimeStart;

        public State CurrentState
        {
            get => currentState;
            set => currentState = value;
        }

        /// <summary>
        /// Start this instance adds to world mover and searches for collider streamer prefab.
        /// </summary>
        private void Awake()
        {
            Setup();
        }

        private void Setup()
        {
            if (CurrentState != State.Start)
                return;

            colliderStreamerManager = GameObject.FindGameObjectWithTag(ColliderStreamerManager.COLLIDERSTREAMERMANAGERTAG).GetComponent<ColliderStreamerManager>();

            colliderStreamerManager.AddColliderStreamer(this);

            CurrentState = State.Ready;
        }

        /// <summary>
        /// Sets the scene game object and moves it to collider streamer position
        /// </summary>
        /// <param name="sceneGameObject">Scene game object.</param>
        public void SetSceneGameObject(GameObject sceneGameObject)
        {
            this.sceneGameObject = sceneGameObject;
            this.sceneGameObject.transform.position = transform.position;
            CurrentState = State.Loaded;
        }


        /// <summary>
        /// Raises the trigger enter event and loads collider scene
        /// </summary>
        /// <param name="other">Other.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (CurrentState == State.Start)
                Setup();

            //Debug.Log($"On trigger enter {other.transform.name}", this);

            if (playerOnlyActivate && other.transform != colliderStreamerManager.player) return;

            if (CurrentState is State.Loading or State.Loaded)
                return;


            CurrentState = State.Loading;

            if (Streamer.loadingManager != null)
            {
                //Debug.Log($"Streamer load scene async {sceneName}", this);
                Streamer.loadingManager.LoadColliderAsync(this);
            }
            else
            {
                //Debug.Log($"SceneManager load scene async {sceneName}", this);
                SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            }
        }

        /// <summary>
        /// Raises the trigger exit event, and destroys scene game object
        /// </summary>
        /// <param name="other">Other.</param>
        private void OnTriggerExit(Collider other)
        {
            if (CurrentState is State.Unloading or State.Unloaded)
                return;

            if ((playerOnlyActivate && other.transform != colliderStreamerManager.player) || !sceneGameObject) return;

            CurrentState = State.Unloading;

            //Debug.Log($"Unload scene async {sceneGameObject.scene.name}", this);
            //Invoke(nameof(UnloadScene), unloadTimer);
            _unloadTimeStart = Time.time;
        }

        /// <summary>
        /// Update this instance. Unloads scene after unloadTimer seconds
        /// </summary>
        private void Update()
        {
            //if (_waitingForUnload)
            //    Debug.Log($"time {Time.time} unloadTimeStart {_unloadTimeStart} {Time.time - _unloadTimeStart}", this);
            if (CurrentState == State.Unloading && Time.time - _unloadTimeStart > unloadTimer)
                UnloadScene();
        }

        /// <summary>
        /// Unloads the scene.
        /// </summary>
        public void UnloadScene()
        {
            CurrentState = State.Unloaded;
            if (Streamer.loadingManager != null)
            {
                Streamer.loadingManager.UnloadColliderAsync(this);
            }
            else
                SceneManager.UnloadSceneAsync(sceneGameObject.scene);
        }
    }
}