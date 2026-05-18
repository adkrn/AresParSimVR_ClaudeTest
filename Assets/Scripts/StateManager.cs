using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StateManager : MonoBehaviour
{
    public static StateManager Inst { get; private set; }
    
    private TrainingState _trainingState;
    
    private void Awake()
    {
        Inst = this;
    }
    
    public void ReceiveTrainingState(TrainingState state)
    {
        switch (state)
        {
            case TrainingState.Ready:
            {
                WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Ready;
                AresHardwareService.Inst.ResetHardware();
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                break;
            }
            case TrainingState.Start:
            {
                Debug.Log("훈련 시작 요청 받음");

                // DataManager의 데이터 로딩 상태 확인
                if (DataManager.Inst.IsDataLoaded)
                {
                    Debug.Log("데이터 로딩 완료 - 훈련 시작 가능 상태로 응답");
                    // 데이터가 준비되었으므로 Start 상태로 응답
                    WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Start;
                    AresHardwareService.Inst.SetEvent(AresEvent.SitDown);
                }
                else if (DataManager.Inst.IsLoadingData)
                {
                    Debug.LogWarning("데이터 로딩 중 - Ready 상태로 응답");
                    // 데이터 로딩 중이므로 Ready 상태로 응답
                    WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Ready;
                }
                else
                {
                    Debug.LogError("데이터가 로드되지 않았습니다 - Ready 상태로 응답");
                    // 데이터가 없으므로 Ready 상태로 응답
                    WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Ready;
                }
                break;
            }
            case TrainingState.Pause:
            {
                if (_trainingState != TrainingState.Start && _trainingState != TrainingState.Resume) return;
                
                WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Pause;
                UIManager.Inst.ShowPauseUI();
                break;
            }
            case TrainingState.Resume:
            {
                if (_trainingState != TrainingState.Pause) return;
                
                WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.Resume;
                UIManager.Inst.HidePauseUI();
                break;
            }
            case TrainingState.End:
            {
                if (_trainingState != TrainingState.Start)
                {
                    Debug.LogWarning("훈련 종료 처리 정보 : 현재 훈련중인 상태가 아니므로 종료 상태로 바꾸지 않습니다.");
                    return;
                }
                WS_DB_Client.Instance.CurParticipantData.trainingState = TrainingState.End;
                AresHardwareService.Inst.SetEvent(AresEvent.None);
                SceneLoadManager.Inst.LoadLobbyScene();
                break;
            }
        }
        
        _trainingState = state;
        WS_DB_Client.Instance.SendTraningStateResponse(false, WS_DB_Client.Instance.CurParticipantData);
    }
}
