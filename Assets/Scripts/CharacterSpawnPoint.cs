using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬별 캐릭터 스폰 위치
/// 이 GameObject의 위치와 계층이 캐릭터의 배치 위치가 됨
/// </summary>
public class CharacterSpawnPoint : MonoBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("캐릭터를 찾을 태그 (Player)")]
    [SerializeField] private string characterTag = "Player";

    [Tooltip("부모 기준 로컬 위치")]
    [SerializeField] private Vector3 localPosition = Vector3.zero;

    [Tooltip("부모 기준 로컬 회전 (Euler 각도)")]
    [SerializeField] private Vector3 localRotationEuler = Vector3.zero;

    [Header("Optional")]
    [Tooltip("배치 후 Rigidbody 속도 리셋 (낙하 중 전환 시)")]
    [SerializeField] private bool resetVelocity = false;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private GameObject character;

    void Start()
    {
        //PlaceCharacter();
    }

    private void PlaceCharacter()
    {
        // DontDestroyOnLoad된 캐릭터 찾기
        character = GameObject.FindWithTag(characterTag);

        if (character == null)
        {
            if (debugLog)
                Debug.LogWarning($"[CharacterSpawn] Character with tag '{characterTag}' not found in scene: {gameObject.scene.name}");
            return;
        }

        // 이 GameObject를 부모로 설정
        character.transform.SetParent(transform, false);

        // 로컬 위치/회전 설정
        character.transform.localPosition = localPosition;
        character.transform.localRotation = Quaternion.Euler(localRotationEuler);

        // 선택적: Physics 속도 리셋
        if (resetVelocity)
        {
            Rigidbody rb = character.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (debugLog)
        {
            Debug.Log($"[CharacterSpawn] Character placed in scene: {gameObject.scene.name} " +
                      $"Parent: {transform.parent?.name ?? "Root"} " +
                      $"World Position: {character.transform.position}");
        }
    }
}
