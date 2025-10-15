using UnityEngine;

public class CloudAnim : MonoBehaviour
{
    // 파티클 시스템 참조
    [SerializeField] private ParticleSystem particleEffect;

    // 재생 및 일시 정지 시간 설정
    [SerializeField] private float playDuration = 5.0f;
    [SerializeField] private float pauseDuration = 2.0f;
    
    // 파티클 크기 설정
    [SerializeField] private float activeSize = 8.0f;
    [SerializeField] private float inactiveSize = 0.0f;

    // 타이머 및 상태 관리 변수
    private float timer = 0.0f;
    private bool isActive = true;
    private ParticleSystem.MainModule mainModule;

    private void Awake()
    {
        // 파티클 시스템을 찾지 못했을 경우 자동으로 현재 게임 오브젝트에서 찾기
        if (particleEffect == null)
        {
            particleEffect = GetComponent<ParticleSystem>();

            // 컴포넌트가 없는 경우 경고 메시지 출력
            if (particleEffect == null)
            {
                Debug.LogWarning("파티클 시스템이 CloudAnim 스크립트에 할당되지 않았습니다. Inspector에서 할당하거나 같은 게임 오브젝트에 추가해주세요.");
            }
        }
        
        // 파티클 시스템이 있다면 MainModule 초기화
        if (particleEffect != null)
        {
            mainModule = particleEffect.main;
            // 시작시 항상 활성화된 크기로 설정
            mainModule.startSize = activeSize;
            particleEffect.Play(); // 파티클 시스템 항상 재생 상태 유지
            //Debug.Log("파티클 시스템 초기화: 크기 " + activeSize);
        }
    }

    void Update()
    {
        // 파티클 시스템이 없으면 아무 작업도 수행하지 않음
        if (particleEffect == null)
            return;
            
        // 타이머 업데이트
        timer += Time.deltaTime;
        
        if (isActive)
        {
            // 활성화 상태 (크기 = activeSize)
            // 크기가 activeSize가 아니라면 설정
            if (mainModule.startSize.constant != activeSize)
            {
                mainModule.startSize = activeSize;
                // Debug.Log("파티클 크기 변경: " + activeSize);
            }
            
            // 활성화 시간이 지나면 비활성화 상태로 전환
            if (timer >= playDuration)
            {
                isActive = false;
                mainModule.startSize = inactiveSize;
                timer = 0.0f;
                // Debug.Log("파티클 비활성화: 크기 " + inactiveSize);
            }
        }
        else
        {
            // 비활성화 상태 (크기 = inactiveSize)
            // 크기가 inactiveSize가 아니라면 설정
            if (mainModule.startSize.constant != inactiveSize)
            {
                mainModule.startSize = inactiveSize;
                // Debug.Log("파티클 크기 변경: " + inactiveSize);
            }
            
            // 비활성화 시간이 지나면 활성화 상태로 전환
            if (timer >= pauseDuration)
            {
                isActive = true;
                mainModule.startSize = activeSize;
                timer = 0.0f;
                // Debug.Log("파티클 활성화: 크기 " + activeSize);
            }
        }
    }
}
