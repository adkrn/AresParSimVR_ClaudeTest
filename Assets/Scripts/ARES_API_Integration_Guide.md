# ARES 낙하산 시뮬레이터 API 통합 가이드 (업데이트)

## 개요
ARES 낙하산 시뮬레이터 하드웨어가 ParagliderController에 직접 통합되었습니다.
별도의 AresParachuteController가 필요하지 않으며, ParagliderController에서 모든 기능을 제공합니다.

## 구성 요소

### 1. AresParachuteAPI.cs
- **역할**: DLL과의 통신을 담당하는 저수준 API 래퍼
- **주요 기능**:
  - 시뮬레이터 초기화/해제
  - 모션 데이터 전송
  - 피드백 데이터 수신
  - 확장 API (MotionControlEx) 지원

### 2. ParagliderController.cs (통합됨)
- **역할**: 낙하산 물리 시뮬레이션 + ARES 하드웨어 제어
- **주요 기능**:
  - 기존의 모든 낙하산 물리 시뮬레이션
  - ARES 하드웨어 옵션 제어 (켜기/끄기 가능)
  - 자동 상태 동기화
  - 피드백 데이터 처리

## 사용 방법

### 1. Inspector 설정
ParagliderController 컴포넌트의 ARES Hardware Integration 섹션에서:
- **Use Ares Hardware**: 체크하여 하드웨어 연동 활성화
- **Ares Com Port**: COM 포트 번호 (0 = COM1, 1 = COM2...)
- **Ares Timeout**: 통신 타임아웃 (밀리초)
- **Ares Debug Mode**: 디버그 로그 출력 여부

### 2. 하드웨어 없이 테스트
`Use Ares Hardware`를 체크 해제하면 하드웨어 없이도 게임이 정상 작동합니다.

### 3. 상태 전환
ParagliderController가 자동으로 처리:
- `JumpStart()` → 자유낙하 이벤트 전송
- `ParaDeploy()` → 낙하산 전개 이벤트 전송
- `OnLanding()` → 착륙 시작 이벤트 전송
- `OnLanded()` → 착륙 완료 이벤트 전송

### 4. 추가 기능
```csharp
// 낙하산 고장 시뮬레이션
paragliderController.TriggerMalfunction();

// 바람 효과 설정
paragliderController.SetWindControl(50, 180, 20);
```

## 데이터 흐름

### 게임 → 하드웨어
1. **Roll 각도**: 라이저 입력에 따라 자동 계산
2. **Yaw 각도**: 현재 회전 각도 자동 전송
3. **라이저 강도**: leftPull/rightPull 값 (0~1) → (0~100%)
4. **이벤트**: 상태 변화 시 자동 전송

### 하드웨어 → 게임
1. **Yaw 위치**: 실제 하드웨어의 현재 회전 위치
2. **라이저 감지**: 손잡이 감지 여부 (미감지 시 입력 무시 가능)
3. **라이저 길이**: 실제로 당겨진 라이저 길이

## 장점

### 1. 통합된 구조
- 하나의 컴포넌트에서 모든 낙하산 제어 관리
- 코드 중복 제거
- 유지보수 용이

### 2. 유연한 설정
- Inspector에서 하드웨어 사용 여부 선택
- 하드웨어 없이도 개발/테스트 가능
- 디버그 모드로 상세 로그 확인

### 3. 자동 동기화
- 게임 상태와 하드웨어 상태 자동 동기화
- 별도의 호출 없이 자동으로 데이터 전송

## 주의사항

1. **DLL 파일 위치**: `ARESParaSimDllMotionExternC.dll`은 반드시 `Assets/Plugins/` 폴더에 있어야 합니다.

2. **성능**: FixedUpdate에서 매 프레임 통신하므로 필요시 전송 주기 조절 가능

3. **에러 처리**: 하드웨어 연결이 끊어져도 게임은 계속 진행됩니다.

## 마이그레이션 가이드

기존 AresParachuteController를 사용하던 경우:
1. AresParachuteController 컴포넌트 제거
2. ParagliderController의 `Use Ares Hardware` 체크
3. COM 포트 및 설정 확인
4. 기존 호출 코드 제거 (자동으로 처리됨)