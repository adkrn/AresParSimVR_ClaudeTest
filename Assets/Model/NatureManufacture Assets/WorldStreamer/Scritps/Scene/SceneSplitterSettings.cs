using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace WorldStreamer2
{
    /// <summary>
    /// Stores scene splitter settings.
    /// </summary>
    public class SceneSplitterSettings : MonoBehaviour
    {
        /// <summary>
        /// The scenes split path.
        /// </summary>
        public string scenesPath = "NatureManufacture Assets/WorldStreamer/SplitScenes";

        public List<SceneCollectionManager> sceneCollectionManagers = new List<SceneCollectionManager>();

        public string GetScenesPath()
        {



            string path = scenesPath;

            if (!path.StartsWith("Assets/"))
            {
                if (path.StartsWith("/") || path.StartsWith("\\"))
                    path = "Assets" + scenesPath;
                else
                    path = "Assets/" + scenesPath;



            }



            if (path[path.Length - 1] != '/' && path[path.Length - 1] != '\\')
                path += "/";

            return path;
        }
    }
}