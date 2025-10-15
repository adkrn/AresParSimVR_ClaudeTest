using System;
using UnityEngine;

/// <summary>
/// 충돌만 감지해 델리게이트 호출
/// </summary>
public class TriggerListener : MonoBehaviour
{
    public event Action OnPlayerEntered;
    
    private void Awake()
    {
        Collider col = GetComponent<Collider>() ??
                       gameObject.AddComponent<BoxCollider>();

        if (!col.isTrigger) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(gameObject.tag))
        {
            Debug.Log($"[TriggerListener] {gameObject.tag} 콜라이더 트리거 감지");
            OnPlayerEntered?.Invoke();
        }
    }
}