using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Unity 6.1용 SSL 인증서 핸들러
/// 모든 SSL 인증서를 수락합니다 (개발 환경용)
/// </summary>
public class AcceptAllCertificatesHandler : CertificateHandler
{
    /// <summary>
    /// 모든 인증서를 신뢰
    /// </summary>
    protected override bool ValidateCertificate(byte[] certificateData)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 개발 환경에서는 모든 인증서 수락
        Debug.Log("[AcceptAllCertificatesHandler] SSL 인증서 검증 건너뜀 (개발 모드)");
        
        if (certificateData != null && certificateData.Length > 0)
        {
            Debug.Log($"[AcceptAllCertificatesHandler] 인증서 크기: {certificateData.Length} bytes");
            
            // SHA256 지문 계산 (참고용)
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(certificateData);
                string fingerprint = System.BitConverter.ToString(hash).Replace("-", "");
                Debug.Log($"[AcceptAllCertificatesHandler] SHA256 지문: {fingerprint}");
            }
        }
        
        return true;
#else
        // 프로덕션에서는 기본 검증 사용
        Debug.Log("[AcceptAllCertificatesHandler] 프로덕션 모드 - 기본 검증 사용");
        return base.ValidateCertificate(certificateData);
#endif
    }
}