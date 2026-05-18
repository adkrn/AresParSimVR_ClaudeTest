using UnityEngine;
using UnityEngine.SceneManagement;

public class TrackingSceneTransitionTest : MonoBehaviour
{
    private Scene currentScene;      // 001 씬
    private bool loaded002 = false;  // 002가 이미 로드됐는지 여부

    // 씬 전환 전에 복원할 원래 트랜스폼 정보
    private Transform originalParent;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    private void Start()
    {
        // 001을 현재 씬으로 등록 (이름 기준)
        currentScene = SceneManager.GetSceneByName("001");
        if (!currentScene.IsValid())
        {
            // 예외적으로 이름을 못 찾으면 활성 씬을 사용
            currentScene = SceneManager.GetActiveScene();
        }

        // 현재 오브젝트의 원래 부모/위치/회전 저장
        originalParent   = transform.parent;
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        // 시작 시, 현재 로드된 모든 씬에서 playerPos를 찾아서 하위로 붙이기
        AttachToPlayerPosInAllLoadedScenes();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            if (!loaded002)
            {
                // 1) 씬 전환 전에 원래 위치/회전/부모로 되돌리기
                transform.SetParent(originalParent, true);  // 부모 복원 (null이면 루트)
                transform.position = originalPosition;      // 월드 위치 복원
                transform.rotation = originalRotation;      // 월드 회전 복원

                // 2) 002 씬을 Additive로 로드
                SceneManager.LoadScene("002", LoadSceneMode.Additive);
                loaded002 = true;

                // 3) 001 씬(현재 씬) 언로드
                if (currentScene.IsValid() && currentScene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(currentScene);
                }

                // 4) 현재 로드된 씬들(고정 씬 + 002)에서 다시 playerPos를 찾아 하위로 붙이기
                AttachToPlayerPosInAllLoadedScenes();
            }
        }
    }

    // 현재 로드된 모든 씬에서 "playerPos"를 찾아 이 오브젝트를 자식으로 설정
    private void AttachToPlayerPosInAllLoadedScenes()
    {
        GameObject playerPosObj = null;

        int sceneCount = SceneManager.sceneCount;
        for (int i = 0; i < sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;

            GameObject[] roots = s.GetRootGameObjects();
            foreach (var root in roots)
            {
                playerPosObj = FindInChildrenRecursive(root.transform, "playerPos");
                if (playerPosObj != null)
                    break;
            }

            if (playerPosObj != null)
                break;
        }

        if (playerPosObj != null)
        {
            transform.SetParent(playerPosObj.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogWarning("[TrackingSceneTransitionTest] 어떤 씬에서도 'playerPos'를 찾지 못했습니다.");
        }
    }

    private GameObject FindInChildrenRecursive(Transform parent, string targetName)
    {
        if (parent.name == targetName)
            return parent.gameObject;

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            var found = FindInChildrenRecursive(child, targetName);
            if (found != null)
                return found;
        }

        return null;
    }
}
