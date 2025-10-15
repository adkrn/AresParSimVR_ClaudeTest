using UnityEngine;
using UnityEngine.Networking;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// SSL 인증서 처리를 위한 핸들러 클래스들
/// </summary>
namespace AresParSimVR.Network
{
    /// <summary>
    /// 개발 환경용 - 모든 SSL 인증서를 신뢰 (보안 경고!)
    /// </summary>
    public class BypassCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // 경고: 개발 환경에서만 사용!
            // 모든 인증서를 무조건 신뢰 (MITM 공격 위험)
            Debug.LogWarning("[BypassCertificateHandler] SSL 인증서 검증을 건너뜁니다. 개발 환경에서만 사용하세요!");
            return true;
        }
    }
    
    /// <summary>
    /// 프로덕션용 - 특정 인증서만 신뢰
    /// </summary>
    public class CustomCertificateHandler : CertificateHandler
    {
        private string[] trustedFingerprints;
        
        public CustomCertificateHandler(params string[] fingerprints)
        {
            trustedFingerprints = fingerprints;
        }
        
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // 인증서 지문 계산
            string fingerprint = GetCertificateFingerprint(certificateData);
            
            Debug.Log($"[CustomCertificateHandler] 서버 인증서 지문: {fingerprint}");
            
            // 신뢰할 인증서 목록과 비교
            foreach (var trusted in trustedFingerprints)
            {
                if (fingerprint.Equals(trusted, System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[CustomCertificateHandler] 신뢰할 수 있는 인증서입니다.");
                    return true;
                }
            }
            
            Debug.LogError($"[CustomCertificateHandler] 신뢰할 수 없는 인증서! 지문: {fingerprint}");
            return false;
        }
        
        private string GetCertificateFingerprint(byte[] certificateData)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(certificateData);
                StringBuilder sb = new StringBuilder();
                
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }
                
                return sb.ToString();
            }
        }
    }
    
    /// <summary>
    /// 디버그용 - 인증서 정보를 로그로 출력하고 신뢰
    /// </summary>
    public class DebugCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // 인증서 정보 출력
            string fingerprint = GetCertificateFingerprint(certificateData);
            
            Debug.Log("========== SSL 인증서 정보 ==========");
            Debug.Log($"인증서 크기: {certificateData.Length} bytes");
            Debug.Log($"SHA256 지문: {fingerprint}");
            Debug.Log("=====================================");
            
            // PEM 형식으로 변환하여 출력 (필요시)
            string pemCert = System.Convert.ToBase64String(certificateData);
            Debug.Log($"인증서 (Base64):\n{pemCert}");
            
            // 개발 중에는 신뢰
            return true;
        }
        
        private string GetCertificateFingerprint(byte[] certificateData)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(certificateData);
                return System.BitConverter.ToString(hash).Replace("-", "");
            }
        }
    }
}

/// <summary>
/// SSL 인증서 헬퍼 유틸리티
/// </summary>
public static class SSLCertificateHelper
{
    /// <summary>
    /// 환경에 따라 적절한 인증서 핸들러 반환
    /// </summary>
    public static CertificateHandler GetCertificateHandler()
    {
#if UNITY_EDITOR
        // Unity Editor에서는 인증서 검증 건너뛰기
        Debug.LogWarning("[SSLCertificateHelper] Unity Editor 모드 - SSL 인증서 검증을 건너뜁니다.");
        return new AresParSimVR.Network.BypassCertificateHandler();
        
#elif DEVELOPMENT_BUILD
        // 개발 빌드에서는 디버그 핸들러 사용
        Debug.Log("[SSLCertificateHelper] Development Build - 인증서 정보를 출력합니다.");
        return new AresParSimVR.Network.DebugCertificateHandler();
        
#else
        // 프로덕션 빌드에서는 특정 인증서만 신뢰
        // TODO: 실제 서버 인증서 지문으로 교체 필요
        string[] trustedFingerprints = new string[]
        {
            "YOUR_PRODUCTION_SERVER_CERT_FINGERPRINT_HERE",
            "BACKUP_CERT_FINGERPRINT_IF_NEEDED"
        };
        
        return new AresParSimVR.Network.CustomCertificateHandler(trustedFingerprints);
#endif
    }
    
    /// <summary>
    /// HttpWebRequest용 인증서 검증 콜백 설정
    /// </summary>
    public static void ConfigureHttpWebRequest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 개발 환경에서만 모든 인증서 신뢰
        System.Net.ServicePointManager.ServerCertificateValidationCallback = 
            (sender, certificate, chain, sslPolicyErrors) => 
            {
                Debug.LogWarning("[HttpWebRequest] SSL 인증서 검증을 건너뜁니다 (개발 모드)");
                return true;
            };
#else
        // 프로덕션에서는 기본 검증 사용
        System.Net.ServicePointManager.ServerCertificateValidationCallback = null;
#endif
    }
}