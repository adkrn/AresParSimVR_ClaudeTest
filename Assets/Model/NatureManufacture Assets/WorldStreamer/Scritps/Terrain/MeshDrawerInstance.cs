using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldStreamer2
{
    [ExecuteInEditMode]
    public class MeshDrawerInstance : MonoBehaviour
    {
        public bool noFrustrum = true;
        //public Mesh mesh;
        public MeshDistance[] meshDistances;
        public Matrix4x4[] matrices;
        //public Material material;


        public List<Dictionary<Vector3Int, Cell>> cells;



        void Start()
        {
            GenerateCells();

        }

        private void GenerateCells()
        {
            //int[] posInt = new int[] { posX, posY, posZ };
            // new Dictionary<int[], SceneSplit>(new IntArrayComparer());
            cells = new List<Dictionary<Vector3Int, Cell>>();
            Vector3 pos;

            for (int m = 0; m < meshDistances.Length; m++)
            {

                cells.Add(new Dictionary<Vector3Int, Cell>(new Vector3IntArrayComparer()));

                float cellSize = meshDistances[m].cellSize;

                for (int i = 0; i < matrices.Length; i++)
                {
                    pos = matrices[i].GetColumn(3);
                    int x = (int)Mathf.FloorToInt((pos.x / cellSize));
                    int z = (int)Mathf.FloorToInt((pos.z / cellSize));
                    Vector3Int cellID = new Vector3Int(x, 0, z);

                    if (cells[m].ContainsKey(cellID))
                    {

                        cells[m][cellID].matrices.Add(matrices[i]);
                        cells[m][cellID].bounds.Encapsulate(pos);
                        if (cells[m][cellID].size.x > pos.x)
                        {
                            cells[m][cellID].size.x = pos.x;
                        }
                        if (cells[m][cellID].size.y < pos.x)
                        {
                            cells[m][cellID].size.y = pos.x;
                        }

                        if (cells[m][cellID].size.z > pos.z)
                        {
                            cells[m][cellID].size.z = pos.z;
                        }
                        if (cells[m][cellID].size.w < pos.z)
                        {
                            cells[m][cellID].size.w = pos.z;
                        }
                    }
                    else
                    {
                        cells[m].Add(cellID, new Cell()
                        {
                            matrices = new List<Matrix4x4>(),
                            size = new Vector4(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue),
                            bounds = new Bounds()
                        });
                        cells[m][cellID].matrices.Add(matrices[i]);
                        cells[m][cellID].bounds.Encapsulate(pos);

                        if (cells[m][cellID].size.x > pos.x)
                        {
                            cells[m][cellID].size.x = pos.x;
                        }
                        if (cells[m][cellID].size.y < pos.x)
                        {
                            cells[m][cellID].size.y = pos.x;
                        }

                        if (cells[m][cellID].size.z > pos.z)
                        {
                            cells[m][cellID].size.z = pos.z;
                        }
                        if (cells[m][cellID].size.w < pos.z)
                        {
                            cells[m][cellID].size.w = pos.z;
                        }
                    }

                }
                foreach (var item in cells[m].Keys)
                {
                    cells[m][item].matricesArray = cells[m][item].matrices.ToArray();

                }

            }
        }


        // Update is called once per frame
        void Update()
        {
            Camera mainCam = Camera.main;
            Vector3 pos = mainCam.transform.position;

            if (meshDistances == null)
                return;

            if (cells == null || cells.Count == 0)
            {
                GenerateCells();
                return;
            }


            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCam);

            for (int i = 0; i < meshDistances.Length; i++)
            {
                if (!meshDistances[i].on)
                    continue;
                float cellSize = meshDistances[i].cellSize;

                int distanceCell = (int)Mathf.FloorToInt((meshDistances[i].distance / (float)cellSize)) + 1;
                if (distanceCell > 100)
                    return;
                int previousDistCell = 0;
                if (i > 0)
                    previousDistCell = (int)Mathf.FloorToInt((meshDistances[i - 1].distance * 2 / (float)cellSize));

                int x = (int)Mathf.FloorToInt((pos.x / cellSize));
                int z = (int)Mathf.FloorToInt((pos.z / cellSize));

                Vector3Int cameraCellID = new Vector3Int(x, 0, z);
                Vector3Int cellId = cameraCellID;


                for (int posX = x - distanceCell; posX < x + distanceCell; posX++)
                {
                    for (int posZ = z - distanceCell; posZ < z + distanceCell; posZ++)
                    {

                        cellId.x = posX;
                        cellId.z = posZ;

                        if (Mathf.Abs(posX - x) < previousDistCell && Mathf.Abs(posZ - z) < previousDistCell)
                            continue;

                        if (cells[i].ContainsKey(cellId))
                            for (int j = 0; j < meshDistances[i].meshMaterials.Count; j++)
                            {
                                MeshMaterials meshMaterial = meshDistances[i].meshMaterials[j];
                                bool render = true;


                                render = GeometryUtility.TestPlanesAABB(planes, cells[i][cellId].bounds);

                                if (render || noFrustrum)
                                {
                                    for (int m = 0; m < meshMaterial.materials.Length; m++)
                                    {
                                        Graphics.DrawMeshInstanced(meshMaterial.mesh, m, meshMaterial.materials[m], cells[i][cellId].matricesArray);
                                    }
                                }

                            }
                    }
                }

            }
        }

        void OnDrawGizmosSelected()
        {
            // return;
            Vector3 pos = Camera.main.transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(pos, 5);

            Color[] colors = new Color[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta };


            for (int i = meshDistances.Length - 1; 0 <= i; i--)
            {
                Gizmos.color = Color.yellow;

                //continue;

                if (!meshDistances[i].on)
                    continue;


                Gizmos.DrawWireCube(pos, Vector3.one * meshDistances[i].distance);
                float cellSize = meshDistances[i].cellSize;

                int distanceCell = (int)Mathf.FloorToInt((meshDistances[i].distance / (float)cellSize)) + 1;
                if (distanceCell > 20)
                    return;
                int previousDistCell = 0;
                if (i > 0)
                    previousDistCell = (int)Mathf.FloorToInt((meshDistances[i - 1].distance * 2 / (float)cellSize));

                int x = (int)Mathf.FloorToInt((pos.x / cellSize));
                int z = (int)Mathf.FloorToInt((pos.z / cellSize));

                Vector2Int cameraCellID = new Vector2Int(x, z);

                Vector2Int cellId = cameraCellID;


                for (int posX = x - distanceCell; posX < x + distanceCell; posX++)
                {
                    for (int posY = z - distanceCell; posY < z + distanceCell; posY++)
                    {

                        cellId.x = posX;
                        cellId.y = posY;
                        Gizmos.color = colors[i];

                        // Debug.Log(previousDistCell);

                        if (Mathf.Abs(posX - x) < previousDistCell && Mathf.Abs(posY - z) < previousDistCell)
                        {

                            continue;
                        }

                        Gizmos.DrawWireCube(new Vector3(posX * cellSize + cellSize * 0.5f, pos.y, posY * cellSize + cellSize * 0.5f), Vector3.one * cellSize);
                    }
                }


            }



        }



    }


    [System.Serializable]
    public class MeshDistance
    {
        public List<MeshMaterials> meshMaterials;
        public float distance = 1000;
        public float cellSize = 10;
        public bool on = true;
    }

    [System.Serializable]
    public class Cell
    {
        public List<Matrix4x4> matrices;
        public Matrix4x4[] matricesArray;
        public Bounds bounds;
        public Vector4 size;
    }


    [System.Serializable]
    public class MeshMaterials
    {
        public Mesh mesh;
        public Material[] materials;
    }

}