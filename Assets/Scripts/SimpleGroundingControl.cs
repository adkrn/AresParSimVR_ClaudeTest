using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class SimpleGroundingControl : MonoBehaviour
{
     [Header("Rig Reference")]
        [SerializeField] private Rig rig;

        [Header("Settings")]
        [SerializeField] private float fadeSpeed = 3f;

        [SerializeField] private bool isGrounded = true;

        private RigBuilder rigBuilder;

        private void Awake()
        {
            // Rig Builder를 먼저 비활성화 (RetargetingLayer 에러 방지)
            rigBuilder = GetComponent<RigBuilder>();
            if (rigBuilder != null)
            {
                rigBuilder.enabled = false;
            }
        }

        private void Start()
        {
            // Start에서 Rig Builder 활성화
            if (rigBuilder != null)
            {
                rigBuilder.enabled = true;
            }

            // 기본 활성화
            if (rig != null)
                rig.weight = 1f;
        }
    
        /// <summary>
        /// Grounding 활성화/비활성화
        /// </summary>
        public void SetGroundingEnabled(bool enabled)
        {
            if (rig == null) return;
    
            // 부드러운 전환
            StopAllCoroutines();
            StartCoroutine(FadeRigWeight(enabled ? 1f : 0f));
    
            Debug.Log($"[Grounding] {(enabled ? "활성화" : "비활성화")}");
        }
    
        /// <summary>
        /// 즉시 비활성화 (낙하 시작 시)
        /// </summary>
        public void DisableImmediate()
        {
            if (rig != null)
            {
                StopAllCoroutines();
                rig.weight = 0f;
            }
            Debug.Log("[Grounding] 즉시 비활성화");
        }
    
        /// <summary>
        /// Rig Weight 페이드
        /// </summary>
        private IEnumerator FadeRigWeight(float targetWeight)
        {
            float startWeight = rig.weight;
            float elapsed = 0f;
            float duration = 1f / fadeSpeed;
    
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                rig.weight = Mathf.Lerp(startWeight, targetWeight, elapsed / duration);
                yield return null;
            }
    
            rig.weight = targetWeight;
        }
}
