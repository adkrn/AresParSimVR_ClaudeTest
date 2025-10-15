// C# 9.0
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using System.Threading.Tasks;

public class AnimSequenceCheck
{
    private static Animator _anim;
    private static Action _triggerAction;

    /// <summary>
    /// 외부에서 호출해 배치(Deploy) 시퀀스를 시작한다.
    /// 애니메이터 트리거를 이용하여 작동시 사용되는
    /// 애니메이션의 시간을 측정하여 그 시간이후에
    /// 실행될 메서드를 첨부하여 작동하게 처리
    /// </summary>
    /// <param name="anim"></param>
    /// <param name="triggerName"></param>
    /// <param name="triggerAction"></param>
    public static void StartTrigger(Animator anim, string triggerName, Action triggerAction)
    {
        Debug.Log("[PlaneMove] Start Trigger: AnimSequenceCheck");
        _anim = anim;
        _triggerAction = triggerAction;
        Debug.Log("[PlaneMove] triggerName: " + triggerName);
        Debug.Log("[PlaneMove] triggerAction: " + triggerAction);
        if (_anim == null)
        {
            Debug.LogError("[PlaneMove] AnimSequenceCheck: Animator reference가 없습니다.");
            return;
        }

        _anim.ResetTrigger(triggerName);
        _anim.SetTrigger(triggerName);
        _ = WaitForDeployAnimationAsync();
    }

    /// <summary>
    /// Animator 상태를 비동기로 감시하여 애니메이션 종료를 감지
    /// </summary>
    private static async Task WaitForDeployAnimationAsync()
    {
        Debug.Log("[PlaneMove] WaitForDeployAnimationAsync 실행");
        const int layer = 0;

        // 1 프레임 대기: SetTrigger 후 실제 전이가 일어나도록 보장
        await Task.Yield();

        // [fact] 전이가 완전히 끝날 때까지 기다려야 length가 정확
        while (_anim.IsInTransition(layer))
            await Task.Yield();

        // [fact] length는 애니메이션 클립의 재생 시간(초)이며, speed가 1이 아닐 때는 speed로 나눠줘야 실제 재생 시간과 맞음
        var stateInfo  = _anim.GetCurrentAnimatorStateInfo(layer);
        var clipLength = stateInfo.length / Mathf.Max(Mathf.Abs(stateInfo.speed), 0.0001f);
        Debug.Log($"[PlaneMove] AnimationLength = {clipLength:F3}s");

        // [fact] Time.deltaTime을 누적하면 Time.timeScale(슬로모션 등)도 자연스럽게 반영 가능
        float elapsed = 0f;
        while (elapsed < clipLength)
        {
            elapsed += Time.deltaTime;
            await Task.Yield();
        }

        Debug.Log("[PlaneMove] 콜백 실행");
        _triggerAction?.Invoke();
    }
}