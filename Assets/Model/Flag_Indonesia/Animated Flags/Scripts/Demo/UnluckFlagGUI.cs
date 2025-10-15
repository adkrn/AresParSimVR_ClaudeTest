namespace UnluckSoftware
{
    using UnityEngine;

    public class UnluckFlagGUI :MonoBehaviour
    {
        [Header("UI & Camera")]
        public Camera guiCamera;
        public GameObject nextButton, prevButton, lightButton, materialButton;
        public TextMesh txt;

        [Header("Assets")]
        public GameObject[] prefabs;
        public Light[] lights;
        public Material[] materials;

        private GameObject activeObj;
        private int counter, lCounter;

        void Start() => Swap();

        void Update()
        {
            if (Input.GetMouseButtonUp(0)) ButtonUp();

            if (Input.GetKeyUp(KeyCode.RightArrow)) Next();
            else if (Input.GetKeyUp(KeyCode.LeftArrow)) Prev();
            else if (Input.GetKeyUp(KeyCode.Space)) ToggleUI();
        }

        void ToggleUI()
        {
            bool isActive = !nextButton.activeInHierarchy;
            nextButton.SetActive(isActive);
            prevButton.SetActive(isActive);
            lightButton.SetActive(isActive);
            materialButton.SetActive(isActive);
            if (txt != null) txt.gameObject.SetActive(isActive);
        }

        void ButtonUp()
        {
            Ray ray = guiCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var go = hit.transform.gameObject;
                if (go == nextButton) Next();
                else if (go == prevButton) Prev();
                else if (go == lightButton) CycleLight();
                else if (go == materialButton) ApplyRandomMaterial();
            }
        }

        void CycleLight()
        {
            if (lights.Length == 0) return;

            lights[lCounter].enabled = false;
            lCounter = (lCounter + 1) % lights.Length;
            lights[lCounter].enabled = true;
        }

        void ApplyRandomMaterial()
        {
            if (materials.Length == 0 || activeObj == null) return;

            var renderer = activeObj.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = materials[Random.Range(0, materials.Length)];
        }

        void Next()
        {
            if (prefabs.Length == 0) return;

            counter = (counter + 1) % prefabs.Length;
            Swap();
        }

        void Prev()
        {
            if (prefabs.Length == 0) return;

            counter = (counter - 1 + prefabs.Length) % prefabs.Length;
            Swap();
        }

        void Swap()
        {
            if (prefabs.Length == 0) return;

            if (activeObj != null)
                Destroy(activeObj);

            activeObj = Instantiate(prefabs[counter]);

            if (txt != null && activeObj != null)
            {
                var nameText = activeObj.name.Replace("(Clone)", "");
                var meshComponent = activeObj.GetComponent<UnluckAnimatedMesh>();
                if (meshComponent != null)
                {
                    nameText += " " + meshComponent.meshContainerFBX.name;
                }

                txt.text = nameText.Replace("_", " ").Replace("Flag ", "");
            }
        }
    }
}
