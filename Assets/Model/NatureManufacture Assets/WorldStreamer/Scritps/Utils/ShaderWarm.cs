using UnityEngine;
using System.Collections;


namespace WorldStreamer2
{
    /// <summary>
    /// Shader warm up.
    /// </summary>
    public class ShaderWarm : MonoBehaviour
    {

        /// <summary>
        /// Start this instance, and warms up shaders.
        /// </summary>
        void Start()
        {
            Shader.WarmupAllShaders();
        }

    }
}