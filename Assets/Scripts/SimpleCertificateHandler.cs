using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;

/// <summary>
/// Unity 구버전을 위한 간단한 SSL 인증서 처리
/// </summary>
public static class SimpleCertificateHandler
{
    private static bool _initialized = false;
    
    /// <summary>
    /// SSL 인증서 검증 초기화 (개발용)
    /// </summary>
    public static void InitializeForDevelopment()
    {
        if (_initialized) return;
        
        Debug.LogWarning("[SimpleCertificateHandler] 개발 모드 - SSL 인증서 검증을 비활성화합니다.");
        Debug.LogWarning("경고: 이 설정은 보안 위험이 있습니다. 프로덕션에서는 사용하지 마세요!");
        
        // ServicePointManager를 통한 전역 SSL 검증 설정
        ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
        
        // TLS 버전 설정 (Unity가 오래된 TLS를 사용할 수 있음)
        ServicePointManager.SecurityProtocol = 
            SecurityProtocolType.Tls | 
            SecurityProtocolType.Tls11 | 
            SecurityProtocolType.Tls12;
        
        _initialized = true;
    }
    
    /// <summary>
    /// 프로덕션용 초기화 (특정 인증서만 신뢰)
    /// </summary>
    public static void InitializeForProduction(string[] trustedThumbprints)
    {
        if (_initialized) return;
        
        Debug.Log("[SimpleCertificateHandler] 프로덕션 모드 - 지정된 인증서만 신뢰합니다.");
        
        ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
        {
            if (cert == null) return false;
            
            // 인증서 지문 확인
            string thumbprint = cert.GetCertHashString();
            
            foreach (var trusted in trustedThumbprints)
            {
                if (string.Equals(thumbprint, trusted, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[SimpleCertificateHandler] 신뢰할 수 있는 인증서: {thumbprint}");
                    return true;
                }
            }
            
            Debug.LogError($"[SimpleCertificateHandler] 신뢰할 수 없는 인증서: {thumbprint}");
            return false;
        };
        
        ServicePointManager.SecurityProtocol = 
            SecurityProtocolType.Tls | 
            SecurityProtocolType.Tls11 | 
            SecurityProtocolType.Tls12;
        
        _initialized = true;
    }
    
    /// <summary>
    /// 인증서 검증 콜백 (개발용)
    /// </summary>
    private static bool ValidateServerCertificate(
        object sender, 
        X509Certificate certificate, 
        X509Chain chain, 
        SslPolicyErrors sslPolicyErrors)
    {
        // 개발 환경에서는 모든 인증서 신뢰
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (certificate != null)
        {
            Debug.Log($"[SimpleCertificateHandler] 서버 인증서 정보:");
            Debug.Log($"  - Subject: {certificate.Subject}");
            Debug.Log($"  - Issuer: {certificate.Issuer}");
            Debug.Log($"  - Thumbprint: {certificate.GetCertHashString()}");
            Debug.Log($"  - Valid: {certificate.GetEffectiveDateString()} ~ {certificate.GetExpirationDateString()}");
        }
        
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            Debug.LogWarning($"[SimpleCertificateHandler] SSL 정책 오류: {sslPolicyErrors}");
            Debug.LogWarning("개발 모드이므로 무시하고 진행합니다.");
        }
        
        return true; // 모든 인증서 신뢰
#else
        // 프로덕션에서는 오류가 없는 경우만 신뢰
        return sslPolicyErrors == SslPolicyErrors.None;
#endif
    }
    
    /// <summary>
    /// 현재 서버의 인증서 정보 가져오기 (디버깅용)
    /// </summary>
    public static void GetServerCertificateInfo(string url)
    {
        try
        {
            var request = WebRequest.Create(url) as HttpWebRequest;
            if (request != null)
            {
                request.Method = "HEAD";
                request.Timeout = 5000;
                
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (cert != null)
                    {
                        Debug.Log("========== 서버 인증서 정보 ==========");
                        Debug.Log($"URL: {url}");
                        Debug.Log($"Subject: {cert.Subject}");
                        Debug.Log($"Issuer: {cert.Issuer}");
                        Debug.Log($"Thumbprint: {cert.GetCertHashString()}");
                        Debug.Log($"Serial: {cert.GetSerialNumberString()}");
                        Debug.Log($"Valid: {cert.GetEffectiveDateString()} ~ {cert.GetExpirationDateString()}");
                        Debug.Log($"Format: {cert.GetFormat()}");
                        Debug.Log($"Key Algorithm: {cert.GetKeyAlgorithm()}");
                        Debug.Log("=====================================");
                        
                        // 프로덕션에서 사용할 thumbprint를 클립보드에 복사하려면:
                        Debug.Log($"[프로덕션용 Thumbprint] \"{cert.GetCertHashString()}\"");
                    }
                    return true;
                };
                
                using (var response = request.GetResponse()) { }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SimpleCertificateHandler] 인증서 정보 가져오기 실패: {e.Message}");
        }
    }
    
    /// <summary>
    /// SSL 설정 초기화 해제
    /// </summary>
    public static void Reset()
    {
        ServicePointManager.ServerCertificateValidationCallback = null;
        _initialized = false;
    }
}