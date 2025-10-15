using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldStreamer2
{
    public class StreamerLoadingManager
    {
        private enum SceneType
        {
            Scene,
            Collider,
            SceneSplit
        }

        private struct SceneToLoad
        {
            public SceneType SceneType;
            public string SceneName;
            public SceneSplit SceneSplit;
            public ColliderStreamer ColliderStreamer;
        }

        private struct SceneToUnload
        {
            public SceneType SceneType;
            public Scene Scene;
            public SceneSplit SceneSplit;
            public ColliderStreamer ColliderStreamer;
        }

        public Streamer Streamer;

        private readonly List<SceneToUnload> _scenesToUnload = new();


        public int ScenesToUnloadCount => _scenesToUnload.Count;
        private readonly List<SceneToLoad> _scenesToLoad = new();
        public int ScenesToLoadCount => _scenesToLoad.Count;
        private readonly List<AsyncOperation> _asyncOperations = new();
        public int AsyncOperationsCount => _asyncOperations.Count;

        private LoadingState _loadingState = LoadingState.Loading;


        private enum LoadingState
        {
            Loading,
            Unloading
        }

        private bool _operationStarted;

        public void Update()
        {
            //Debug.Log($"_operationStarted {_operationStarted}");
            if (_operationStarted)
                return;

            //Debug.Log($"_loadingState {_loadingState}");

            //Debug.Log(_asyncOperations.Count);
            if (_asyncOperations.Count > 0)
                return;

            //Debug.Log($"_loadingState {_loadingState} {_scenesToLoad.Count} {_scenesToUnload.Count} {_asyncOperations.Count}");

            if (_loadingState == LoadingState.Unloading)
            {
                if (_scenesToLoad.Count > 0)
                {
                    _loadingState = LoadingState.Loading;
                }
                else if (_scenesToUnload.Count > 0)
                {
                    // Debug.Log("scenesToUnload " + scenesToUnload.Count + " " + asyncOperations.Count);
                    _operationStarted = true;
                    Streamer.StartCoroutine(UnloadAsync());
                    //Unload();
                }
            }

            if (_loadingState == LoadingState.Loading)
            {
                if (_scenesToLoad.Count > 0)
                {
                    // Debug.Log("scenesToLoad " + scenesToLoad.Count + " " + asyncOperations.Count);
                    _operationStarted = true;
                    //Load();
                    Streamer.StartCoroutine(LoadAsync());
                }

                if (_scenesToLoad.Count == 0)
                    _loadingState = LoadingState.Unloading;
            }
        }


        private IEnumerator LoadAsync()
        {
            // yield return new WaitForSeconds(0.5f);

            for (int i = 0; i < _scenesToLoad.Count; i++)
            {
                int sceneID = SceneManager.sceneCount;

                AsyncOperation asyncOperation;
                if (_scenesToLoad[i].SceneType == SceneType.SceneSplit)
                {
                    SceneSplit split = _scenesToLoad[i].SceneSplit;

                    if (CheckIfSplitIsUnloaded(split))
                    {
                        GameObject sceneGameObject = split.scene.GetRootGameObjects()[0];
                        Streamer.BaseSplitSetup(split, sceneGameObject);

                        //remove from unload list
                        _scenesToUnload.RemoveAll(x => x.SceneSplit == split);
                    }
                    else
                    {
                        asyncOperation = SceneManager.LoadSceneAsync(split.sceneName, LoadSceneMode.Additive);

                        if (asyncOperation != null)
                        {
                            asyncOperation.completed += (operation) =>
                            {
                                SceneLoadComplete(sceneID, split);
                                OnOperationDone(operation);
                            };
                            _asyncOperations.Add(asyncOperation);
                        }
                    }
                }
                else if (_scenesToLoad[i].SceneType == SceneType.Collider)
                {
                    ColliderStreamer colliderStreamer = _scenesToLoad[i].ColliderStreamer;

                    if (CheckIfColliderIsUnloaded(colliderStreamer))
                    {
                        //remove from unload list
                        _scenesToUnload.RemoveAll(x => x.ColliderStreamer == colliderStreamer);
                        colliderStreamer.SetSceneGameObject(colliderStreamer.sceneGameObject);
                    }
                    else
                    {
                        asyncOperation = SceneManager.LoadSceneAsync(colliderStreamer.sceneName, LoadSceneMode.Additive);

                        if (asyncOperation != null)
                        {
                            asyncOperation.completed += (operation) =>
                            {
                                //
                                OnOperationDone(operation);
                            };
                            _asyncOperations.Add(asyncOperation);
                        }
                    }
                }
                else
                {
                    if (CheckIfSceneIsUnloaded(_scenesToLoad[i].SceneName))
                    {
                        _scenesToUnload.RemoveAll(x => x.Scene.name == _scenesToLoad[i].SceneName);
                    }
                    else
                    {
                        asyncOperation = SceneManager.LoadSceneAsync(_scenesToLoad[i].SceneName, LoadSceneMode.Additive);
                        if (asyncOperation != null)
                        {
                            asyncOperation.completed += OnOperationDone;
                            _asyncOperations.Add(asyncOperation);
                        }
                    }
                }


                yield return null;
            }

            _scenesToLoad.Clear();
            //Debug.Log("Load finished " + asyncOperations.Count);
            _operationStarted = false;
        }

        private bool CheckIfSceneIsUnloaded(string sceneName)
        {
            for (int i = 0; i < _scenesToUnload.Count; i++)
            {
                if (_scenesToUnload[i].SceneType == SceneType.Scene && _scenesToUnload[i].Scene.name == sceneName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CheckIfSplitIsUnloaded(SceneSplit split)
        {
            for (int i = 0; i < _scenesToUnload.Count; i++)
            {
                if (_scenesToUnload[i].SceneSplit == split)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CheckIfColliderIsUnloaded(ColliderStreamer colliderStreamer)
        {
            for (int i = 0; i < _scenesToUnload.Count; i++)
            {
                if (_scenesToUnload[i].SceneType == SceneType.Collider && _scenesToUnload[i].ColliderStreamer == colliderStreamer)
                {
                    return true;
                }
            }

            return false;
        }


        private void SceneLoadComplete(int sceneID, SceneSplit split)
        {
            //Debug.Log(SceneManager.GetSceneAt(sceneID).name);

            Streamer.StartCoroutine(SceneLoadCompleteAsync(sceneID, split));
        }

        private IEnumerator SceneLoadCompleteAsync(int sceneID, SceneSplit split)
        {
            yield return null;
            Streamer.OnSceneLoaded(SceneManager.GetSceneAt(sceneID), split);
        }

        private void OnOperationDone(AsyncOperation asyncOperation)
        {
            //Debug.Log($"onOperationDone {asyncOperation.isDone} {_asyncOperations.Count}");
            Streamer.StartCoroutine(RemoveAsyncOperation(asyncOperation));
        }

        private IEnumerator RemoveAsyncOperation(AsyncOperation asyncOperation)
        {
            yield return null;
            yield return null;
            _asyncOperations.Remove(asyncOperation);
        }


        private IEnumerator UnloadAsync()
        {
            yield return null;

            //Debug.Log(asyncOperations.Count);
            for (int i = 0; i < _scenesToUnload.Count; i++)
            {
                Scene sceneToUnload;
                switch (_scenesToUnload[i].SceneType)
                {
                    case SceneType.Scene:
                        sceneToUnload = _scenesToUnload[i].Scene;
                        break;
                    case SceneType.SceneSplit:
                        sceneToUnload = _scenesToUnload[i].SceneSplit.scene;
                        CheckForColliderStreamer(_scenesToUnload[i].SceneSplit);
                        break;
                    case SceneType.Collider:
                        if (_scenesToUnload[i].ColliderStreamer.sceneGameObject != null)
                        {
                            sceneToUnload = _scenesToUnload[i].ColliderStreamer.sceneGameObject.scene;
                            _scenesToUnload[i].ColliderStreamer.sceneGameObject = null;
                        }
                        else
                            continue;

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                sceneToUnload.GetRootGameObjects()[0].name = $" (Unloading) {sceneToUnload.GetRootGameObjects()[0].name}";
                AsyncOperation asyncOperation = SceneManager.UnloadSceneAsync(sceneToUnload);
                if (asyncOperation != null)
                {
                    asyncOperation.completed += OnOperationDone;
                    _asyncOperations.Add(asyncOperation);
                }

                //Debug.Log($" UnloadAsync {sceneToUnload.name} {_asyncOperations.Count}");

                yield return null;
            }

            _scenesToUnload.Clear();


            //Debug.Log("Unload finished " + _asyncOperations.Count);
            _operationStarted = false;
        }

        private void CheckForColliderStreamer(SceneSplit sceneSplit)
        {
            ColliderStreamer[] colliderStreamers = sceneSplit.scene.GetRootGameObjects()[0].GetComponentsInChildren<ColliderStreamer>();
            for (int i = 0; i < colliderStreamers.Length; i++)
            {
                ColliderStreamer colliderStreamer = colliderStreamers[i];
                if (colliderStreamer.CurrentState != ColliderStreamer.State.Ready
                    && colliderStreamer.CurrentState != ColliderStreamer.State.Unloaded)
                    colliderStreamer.UnloadScene();
            }
        }

        public void UnloadSplitAsync(SceneSplit sceneSplit)
        {
            //Debug.LogWarning($" UnloadSceneAsync {sceneSplit.scene.name}");
            _scenesToUnload.Add(new SceneToUnload() { SceneType = SceneType.SceneSplit, SceneSplit = sceneSplit });
        }

        public void UnloadSceneAsync(Scene scene)
        {
            //Debug.LogWarning($" UnloadSceneAsync {scene.name}");
            _scenesToUnload.Add(new SceneToUnload() { SceneType = SceneType.Scene, Scene = scene });
        }

        public void UnloadColliderAsync(ColliderStreamer colliderStreamer)
        {
            _scenesToUnload.Add(new SceneToUnload() { SceneType = SceneType.Collider, ColliderStreamer = colliderStreamer });
        }


        public void LoadSceneAsync(SceneSplit split)
        {
            //Debug.Log($" LoadSceneAsync {split.sceneName} ");
            _scenesToLoad.Add(new SceneToLoad() { SceneType = SceneType.SceneSplit, SceneSplit = split });
        }

        public void LoadSceneAsync(string sceneName)
        {
            //Debug.Log($" LoadSceneAsync sceneName {sceneName} ");
            _scenesToLoad.Add(new SceneToLoad() { SceneType = SceneType.Scene, SceneName = sceneName });
        }

        public void LoadColliderAsync(ColliderStreamer colliderStreamer)
        {
            _scenesToLoad.Add(new SceneToLoad() { SceneType = SceneType.Collider, ColliderStreamer = colliderStreamer });
        }
    }
}