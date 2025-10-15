using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace WorldStreamer2
{
    public class ColliderStreamerManager : MonoBehaviour
    {
        /// <summary>
        /// The player transform.
        /// </summary>
        [Tooltip("Object that will start loading process after it hits the collider.")]
        public Transform player;

        /// <summary>
        /// Collider Streamer Manager will wait for player spawn and fill it automatically
        /// </summary>
        [Tooltip("Collider Streamer Manager will wait for player spawn and fill it automatically")]
        public bool spawnedPlayer;

        [HideInInspector] public string playerTag = "Player";

        /// <summary>
        /// The tag of collider streamer manager.
        /// </summary>
        public static string COLLIDERSTREAMERMANAGERTAG = "ColliderStreamerManager";

        //[HideInInspector]
        /// <summary>
        /// The collider streamers.
        /// </summary>
        public List<ColliderStreamer> colliderStreamers;


        /// <summary>
        /// Adds the collider streamer.
        /// </summary>
        /// <param name="colliderStreamer">Collider streamer.</param>
        public void AddColliderStreamer(ColliderStreamer colliderStreamer)
        {
            colliderStreamers.Add(colliderStreamer);
        }

        /// <summary>
        /// Adds the collider scene.
        /// </summary>
        /// <param name="colliderScene">Collider scene.</param>
        public void AddColliderScene(ColliderScene colliderScene)
        {
            //Debug.Log($"");
            foreach (var item in colliderStreamers)
            {
                //Debug.Log($"item {item.name} item: {item.sceneName}, colliderScene: {colliderScene.sceneName} ");

                if (item == null || item.sceneName != colliderScene.sceneName) continue;

                //Debug.Log($"state {item.currentState} {item.currentState == ColliderStreamer.State.Loading}");
                if (item.CurrentState != ColliderStreamer.State.Loading)
                    continue;

                //Debug.Log($"found: {item.sceneName}");
                item.SetSceneGameObject(colliderScene.gameObject);
                return;
            }


            //Debug.LogError($"Collider Scene {colliderScene.sceneName} not found in Collider Streamers", colliderScene.gameObject);

            UnloadScene(colliderScene);
        }

        private void UnloadScene(ColliderScene colliderScene)
        {
            Streamer.loadingManager.UnloadSceneAsync(colliderScene.gameObject.scene);
        }

        public void Update()
        {
            CheckPlayer();
        }

        private void CheckPlayer()
        {
            if (!spawnedPlayer || player != null || string.IsNullOrEmpty(playerTag)) return;

            var playerGo = GameObject.FindGameObjectWithTag(playerTag);
            if (playerGo != null)
                player = playerGo.transform;
        }
    }
}