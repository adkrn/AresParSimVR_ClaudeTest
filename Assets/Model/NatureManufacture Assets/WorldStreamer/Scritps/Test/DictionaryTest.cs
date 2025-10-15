using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldStreamer2
{
    public class DictionaryTest : MonoBehaviour
    {

        public Dictionary<int[], SceneSplit> scenesArray;
        public Dictionary<Vector3Int, SceneSplit> scenesArrayV;
        // Start is called before the first frame update
        void Start()
        {

            int size = 100;

            float time = Time.realtimeSinceStartup;

            scenesArray = new Dictionary<int[], SceneSplit>(new IntArrayComparerTest());
            for (int x = -size; x < size; x++)
            {
                for (int y = -size; y < size; y++)
                {
                    for (int z = -size; z < size; z++)
                    {
                        int[] array = new int[] { x, y, z };
                        scenesArray.Add(array, new SceneSplit());
                    }
                }
            }
            float saveTime = Time.realtimeSinceStartup - time;
            Debug.Log("Creation " + (Time.realtimeSinceStartup - time));

            float timeCR = Time.realtimeSinceStartup;
            for (int x = -size; x < size; x++)
            {
                for (int y = -size; y < size; y++)
                {
                    for (int z = -size; z < size; z++)
                    {
                        int[] array = new int[] { x, y, z };
                        if (scenesArray.ContainsKey(array))
                        {

                            SceneSplit split = scenesArray[array];

                        }
                    }
                }
            }
            float saveTimeCR = Time.realtimeSinceStartup - timeCR;
            Debug.Log("Loading " + (Time.realtimeSinceStartup - timeCR));

            float timeV = Time.realtimeSinceStartup;
            scenesArrayV = new Dictionary<Vector3Int, SceneSplit>(new Vector3ArrayComparerTest());
            for (int x = -size; x < size; x++)
            {
                for (int y = -size; y < size; y++)
                {
                    for (int z = -size; z < size; z++)
                    {
                        Vector3Int array = new Vector3Int(x, y, z);
                        scenesArrayV.Add(array, new SceneSplit());
                    }
                }
            }
            float saveTimeV = Time.realtimeSinceStartup - timeV;
            Debug.Log("Creation " + (Time.realtimeSinceStartup - timeV));

            float timeVCR = Time.realtimeSinceStartup;
            for (int x = -size; x < size; x++)
            {
                for (int y = -size; y < size; y++)
                {
                    for (int z = -size; z < size; z++)
                    {
                        Vector3Int array = new Vector3Int(x, y, z);
                        if (scenesArrayV.ContainsKey(array))
                        {

                            SceneSplit split = scenesArrayV[array];

                        }
                    }
                }
            }
            float saveTimeVCR = Time.realtimeSinceStartup - timeVCR;
            Debug.Log("Loading " + (Time.realtimeSinceStartup - timeVCR));
            Debug.Log("Loading Comp " + (saveTime - saveTimeV));
            Debug.Log("Creation Comp " + (saveTimeCR - saveTimeVCR));

            float timeSingle = Time.realtimeSinceStartup;
            int[] arrayT = new int[] { 10, 10, 10 };
            if (scenesArray.ContainsKey(arrayT))
            {

                SceneSplit split = scenesArray[arrayT];

            }
            float savetimeSingle = Time.realtimeSinceStartup - timeSingle;
            Debug.Log("Single " + savetimeSingle);

            float timeSingleV = Time.realtimeSinceStartup;
            Vector3Int arrayV = new Vector3Int(10, 10, 10);
            if (scenesArrayV.ContainsKey(arrayV))
            {

                SceneSplit split = scenesArrayV[arrayV];

            }
            float savetimeSingleV = Time.realtimeSinceStartup - timeSingleV;
            Debug.Log("Single V " + savetimeSingleV);
            Debug.Log("Single Comp " + (savetimeSingle - savetimeSingleV));


        }


    }
    /// <summary>
    /// Int array comparer.
    /// </summary>
    public class IntArrayComparerTest : IEqualityComparer<int[]>
    {
        /// <summary>
        /// Equals the specified x and y.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        /// <param name="y">The y coordinate.</param>
        public bool Equals(int[] x, int[] y)
        {
            if (x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets the hash code.
        /// </summary>
        /// <returns>The hash code.</returns>
        /// <param name="obj">Object.</param>
        public int GetHashCode(int[] obj)
        {
            int result = 17;
            for (int i = 0; i < obj.Length; i++)
            {
                unchecked
                {
                    result = result * 23 + obj[i];
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Int array comparer.
    /// </summary>
    public class Vector3ArrayComparerTest : IEqualityComparer<Vector3Int>
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