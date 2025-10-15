# Unity SSL Certificate 오류 해결 가이드

## 🔒 문제 상황
Unity에서 HTTPS 서버 연결 시 SSL CA certificate error 발생
- 개발 환경에서 자체 서명 인증서 사용
- Unity 2021 이후 버전에서 인증서 검증 강화
- `NetworkManager` 및 `WS_DB_Client`에서 발생

## ⚠️ 주의사항
**이 방법은 개발 환경에서만 사용하세요!**
- 프로덕션 환경에서는 유효한 SSL 인증서 필수
- 보안 위험이 있으므로 실제 서비스에서는 절대 사용 금지

## 📝 해결 방법

### 방법 1: UnityWebRequest에 Custom Certificate Handler 추가

```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class BypassCertificateHandler : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        // 개발 환경에서만 사용 - 모든 인증서 허용
        return true;
    }
}

// NetworkManager.cs의 ConnectToServer() 메서드 수정
private IEnumerator ConnectToServer()
{
    string url = $"https://{serverIP}:{serverPort}/";
    Debug.Log($"[NetworkManager] ConnectToServer(): {url}");
    
    UnityWebRequest request = UnityWebRequest.Get(url);
    
    // SSL 인증서 검증 우회 (개발 환경용)
    request.certificateHandler = new BypassCertificateHandler();
    
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.Success)
    {
        OnServerConnected(request.downloadHandler.text);
    }
    else
    {
        Debug.LogError($"[NetworkManager] 서버 연결 실패: {request.error}");
    }
}
```

### 방법 2: ServicePointManager 전역 설정 (HttpWebRequest용)

```csharp
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public class NetworkManager : MonoBehaviour
{
    void Awake()
    {
        // 개발 환경에서만 - 전역 SSL 검증 우회
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        ServicePointManager.ServerCertificateValidationCallback = 
            delegate (object sender, X509Certificate certificate, 
                     X509Chain chain, SslPolicyErrors sslPolicyErrors)
            {
                Debug.LogWarning("[NetworkManager] SSL 인증서 검증 우회 중 (개발 모드)");
                return true; // 모든 인증서 허용
            };
        #endif
        
        // 기존 코드...
    }
}
```

### 방법 3: 조건부 인증서 검증

```csharp
public class SmartCertificateHandler : CertificateHandler
{
    private readonly string[] allowedFingerprints;
    private readonly bool allowAll;

    public SmartCertificateHandler(bool developmentMode = false)
    {
        allowAll = developmentMode;
        // 특정 인증서 지문만 허용 (선택적)
        allowedFingerprints = new string[] 
        {
            "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD"
        };
    }

    protected override bool ValidateCertificate(byte[] certificateData)
    {
        // 개발 모드에서는 모든 인증서 허용
        if (allowAll) 
        {
            Debug.LogWarning("SSL 인증서 검증 우회 (개발 모드)");
            return true;
        }

        // 프로덕션에서는 특정 인증서만 허용
        string fingerprint = GetCertificateFingerprint(certificateData);
        foreach (string allowed in allowedFingerprints)
        {
            if (fingerprint == allowed) return true;
        }

        Debug.LogError($"허용되지 않은 인증서: {fingerprint}");
        return false;
    }

    private string GetCertificateFingerprint(byte[] certificateData)
    {
        var cert = new System.Security.Cryptography.X509Certificates.X509Certificate(certificateData);
        return cert.GetCertHashString();
    }
}
```

## 🔧 NetworkManager.cs 통합 수정

```csharp
// NetworkManager.cs 상단에 추가
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public class NetworkManager : MonoBehaviour
{
    // 개발 모드 플래그 추가
    [Header("개발 설정")]
    [SerializeField] private bool bypassSSL = true; // Inspector에서 설정 가능
    
    private void Awake()
    {
        // SSL 인증서 검증 설정
        ConfigureSSL();
        
        if (Inst == null)
        {
            Inst = this;
            DontDestroyOnLoad(gameObject);
            Init();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void ConfigureSSL()
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (bypassSSL)
        {
            // HttpWebRequest용 전역 설정
            ServicePointManager.ServerCertificateValidationCallback = 
                (sender, certificate, chain, sslPolicyErrors) => true;
            
            Debug.LogWarning("[NetworkManager] SSL 인증서 검증 비활성화 (개발 모드)");
        }
        #endif
    }

    private IEnumerator ConnectToServer()
    {
        string url = $"https://{serverIP}:{serverPort}/";
        Debug.Log($"[NetworkManager] ConnectToServer(): {url}");
        
        UnityWebRequest request = UnityWebRequest.Get(url);
        
        // UnityWebRequest용 인증서 핸들러
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (bypassSSL)
        {
            request.certificateHandler = new BypassCertificateHandler();
        }
        #endif
        
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            OnServerConnected(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[NetworkManager] 서버 연결 실패: {request.error}");
        }
    }
    
    // Custom Certificate Handler 클래스 (내부 클래스로 정의)
    public class BypassCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true; // 모든 인증서 허용
        }
    }
}
```

## 🚀 적용 방법

1. **Unity Editor에서**:
   - `NetworkManager` GameObject 선택
   - Inspector에서 `Bypass SSL` 체크박스 활성화
   - Play 모드 실행

2. **빌드 설정**:
   - Development Build 체크 시 자동으로 SSL 우회 활성화
   - Release Build에서는 자동으로 비활성화

3. **WS_DB_Client에도 동일하게 적용**:
```csharp
// WS_DB_Client.cs에서 WebSocket 연결 시
private void ConfigureWebSocketSSL()
{
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
    // WebSocket SSL 설정 (라이브러리에 따라 다름)
    ServicePointManager.ServerCertificateValidationCallback = 
        (sender, certificate, chain, sslPolicyErrors) => true;
    #endif
}
```

## 🔍 문제 해결

### 여전히 SSL 오류가 발생하는 경우:

1. **Unity 버전 확인**:
   - Unity 2021.2 이후: `certificateHandler` 필수
   - Unity 2020.3 이전: `ServicePointManager`만으로 충분

2. **TLS 버전 설정**:
```csharp
ServicePointManager.SecurityProtocol = 
    SecurityProtocolType.Tls12 | 
    SecurityProtocolType.Tls11 | 
    SecurityProtocolType.Tls;
```

3. **방화벽/프록시 확인**:
   - 포트 3000이 열려있는지 확인
   - 프록시 설정이 올바른지 확인

## ✅ 테스트 방법

1. Unity Editor Console에서 확인:
   - `"SSL 인증서 검증 비활성화 (개발 모드)"` 메시지 확인
   - 연결 성공 메시지 확인

2. 프로덕션 빌드 테스트:
   - `bypassSSL = false` 설정
   - 유효한 SSL 인증서로 테스트

## 📌 권장사항

1. **개발 환경**: `bypassSSL = true` 사용
2. **테스트 환경**: 자체 서명 인증서 + `bypassSSL = true`
3. **프로덕션**: 유효한 SSL 인증서 + `bypassSSL = false`

## 🔐 보안 고려사항

- **절대 프로덕션에서 SSL 우회 사용 금지**
- 개발 완료 후 반드시 `bypassSSL = false` 설정
- 빌드 전 `#if UNITY_EDITOR` 조건 확인
- 실제 서비스에서는 Let's Encrypt 등 무료 인증서 사용 권장