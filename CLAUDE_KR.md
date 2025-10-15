# CLAUDE.md (한국어)

이 파일은 이 저장소의 코드 작업 시 Claude Code (claude.ai/code)에 대한 지침을 제공합니다.

## 프로젝트 개요

AresParSimVR은 전문 군사/항공 훈련을 위해 설계된 Unity 기반 VR 낙하산 훈련 시뮬레이터입니다. 이 프로젝트는 세 가지 애플리케이션 모드를 지원합니다:
- **ControlManager**: 교관 제어 인터페이스
- **Simulator**: 훈련생을 위한 메인 VR 훈련 시뮬레이션
- **ViewManager**: 관찰/모니터링 인터페이스

## 빌드 및 개발 명령어

### Unity 에디터
- Unity 2023.x에서 프로젝트 열기 (Universal Render Pipeline 필요)
- 메인 씬: 
  - `Assets/Scenes/WIP/Lobby_0714_01.unity` (진입점)
  - `Assets/Scenes/WIP/Main_0721_01.unity` (메인 시뮬레이션)

### 빌드 설정
- 플랫폼: Android (Meta Quest/Oculus)
- XR 플러그인: Oculus XR Plugin
- 렌더링: Universal Render Pipeline (URP)

### VR 기능 테스트
- PC 기반 테스트를 위해 Meta Quest Link 사용
- 독립 실행형 테스트를 위해 Build & Run으로 Quest 기기에 배포

## 아키텍처 개요

### 핵심 시스템

#### 상태 관리 흐름
애플리케이션은 타임라인 기반 상태 시스템을 사용합니다:
1. **MainManager**가 시작 시 앱 타입(Control/Sim/View)을 결정
2. **StateManager_New**가 CSV로 정의된 타임라인을 통해 훈련 흐름 관리
3. 각 타임라인은 순서대로 진행되는 절차들을 포함 (예: "sit_down", "jump", "parachute_deploy")
4. 절차는 **CompleteCondition** 타입에 따라 실행됨 (Time, Animation, Item, SitDown, Stand, Fall, PullCord, Landing)

#### 데이터 아키텍처
- **DataManager** (싱글톤): CSV 설정을 로드하는 중앙 데이터 허브
- `Assets/StreamingAssets/Csvs/`의 CSV 파일들이 모든 훈련 매개변수 정의
- **WS_DB_Client**: 백엔드 데이터베이스와의 WebSocket 통신

#### 물리 시뮬레이션
- **PlayCharacter**: 항력 계산을 포함한 현실적인 자유낙하 물리 구현
- **ParagliderController**: 낙하산 물리 및 제어 입력 처리
- 실제 물리 상수 사용: 중력 (9.80665 m/s²), 공기 밀도, 항력 계수

### 주요 패턴

#### 싱글톤 사용
```csharp
DataManager.Inst.scenario  // 시나리오 데이터 접근
UIManager.Inst.OnSuccessAction()  // UI 콜백
```

#### 절차 실행
절차는 `StateManager_New.CompleteTriggerAction()`에서 CompleteCondition에 따라 switch문으로 처리됩니다:
```csharp
switch (_currentProcedure.completeCondition)
{
    case CompleteCondition.SitDown:
        _character?.SitDown();
        break;
    case CompleteCondition.Fall:
        StartFreeFall();
        break;
    // 기타 등등
}
```

참고: 일부 레거시 코드에서는 여전히 `[ProcedureHandler]` 속성을 사용하지만 StateManager_New에서는 활용하지 않습니다.

#### 성능 최적화
- FindAnyObjectByType 호출을 피하기 위한 컴포넌트 캐싱
- 프레임당 오버헤드를 줄이기 위한 조건부 Update 패턴
- 네트워크 메시지를 위한 비할당 JSON 직렬화

### 네트워크 아키텍처
- 멀티플레이어 동기화를 위한 Unity Netcode
- 교관-훈련생 영상/음성을 위한 WebRTC
- 실시간 데이터 업데이트 및 모니터링을 위한 WebSocket

### VR 통합
- Quest 통합을 위한 Meta XR SDK
- 컨트롤러 처리를 위한 OVRInput
- 다양한 비행 단계에서 몰입감을 위한 카메라 노이즈 효과

## 일반적인 개발 작업

### 새로운 절차 추가
1. `CD_Procedure.csv`에 적절한 CompleteCondition과 함께 절차 추가
2. `CD_TimeLine.csv`에 해당 타임라인 항목 추가
3. 필요시 `StateManager_New.CompleteTriggerAction()`에 새로운 CompleteCondition case 추가
4. 해당 클래스(PlayCharacter, CameraController 등)에 절차 로직 구현

### 점프 물리 수정
- `PlayCharacter.cs`에서 항력 계수 편집
- `ParagliderController.cs`에서 종단 속도 조정
- `WindManager.cs`에서 바람 효과 설정

### 경로 관리
- `CD_Routes.csv`에 웨이포인트와 함께 경로 정의
- `RouteManager`가 경로 포인트 인스턴스화
- `AirPlane`이 0번 포인트부터 순차적으로 경로 따라감

### 평가 시스템
- `CD_Evaluation.csv`의 평가 기준
- `UIManager.AddResult()`를 통한 결과 추적
- `StateManager_New.OnEnd()`에서 최종 점수 계산

## 최근 변경사항 및 활발한 개발

- 고공 강하 절차 구현
- 향상된 평가 지표 (플레어 사용, 착지 정확도)
- 경로 기반 항공기 이동 시스템
- PlayCharacter와 ParagliderController의 성능 최적화

## 데이터 모델

`DataModel.cs`의 주요 데이터 클래스:
- `Scenario`: 훈련 시나리오 설정
- `TimeLine`: 절차의 순서
- `Procedure`: 개별 훈련 단계
- `Evaluation`: 점수 기준
- `Route`: 항공기 웨이포인트 데이터

CSV 데이터는 점프 유형(STANDARD, HIGH_FALL 등)별로 로드되고 필터링되어 시나리오별 훈련을 제공합니다.