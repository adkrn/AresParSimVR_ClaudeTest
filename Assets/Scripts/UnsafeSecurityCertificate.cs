using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Reflection;

/// <summary>
/// Unity의 SSL 인증서 검증을 우회하는 클래스
/// 주의: 개발 환경에서만 사용하세요!
/// </summary>
public static class UnsafeSecurityCertificate
{
    /// <summary>
    /// UnityWebRequest의 SSL 검증을 비활성화 (Unity 2018.1+)
    /// </summary>
    public static void DisableSSLVerification(UnityWebRequest request)
    {
        try
        {
            // Unity 2018.1+ : certificateHandler 사용
            var certificateHandlerType = Type.GetType("UnityEngine.Networking.CertificateHandler, UnityEngine.UnityWebRequestModule");
            if (certificateHandlerType != null)
            {
                var handler = new BypassCertificate();
                request.certificateHandler = handler;
                Debug.Log("[UnsafeSecurityCertificate] CertificateHandler를 사용하여 SSL 검증 비활성화");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnsafeSecurityCertificate] CertificateHandler 설정 실패: {e.Message}");
        }

        // Unity 2017 이하: useConscrypt 사용 시도
        try
        {
            var property = typeof(UnityWebRequest).GetProperty("useConscrypt");
            if (property != null)
            {
                property.SetValue(request, false);
                Debug.Log("[UnsafeSecurityCertificate] useConscrypt를 false로 설정");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnsafeSecurityCertificate] useConscrypt 설정 실패: {e.Message}");
        }

        Debug.LogWarning("[UnsafeSecurityCertificate] SSL 검증 비활성화 실패 - HTTP 사용을 고려하세요");
    }

    /// <summary>
    /// 전역 SSL 설정 (System.Net 사용)
    /// </summary>
    public static void SetupGlobalSSL()
    {
        // ServicePointManager 설정
        System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        
        // TLS 버전 설정
        System.Net.ServicePointManager.SecurityProtocol = 
            System.Net.SecurityProtocolType.Tls |
            System.Net.SecurityProtocolType.Tls11 |
            System.Net.SecurityProtocolType.Tls12;

        Debug.Log("[UnsafeSecurityCertificate] 전역 SSL 설정 완료 (System.Net)");
    }

    /// <summary>
    /// Unity 내부 SSL 검증 플래그 비활성화 시도
    /// </summary>
    public static void TryDisableUnitySSLCheck()
    {
        try
        {
            // Unity 내부 플래그 찾기
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.Contains("UnityEngine"))
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.Name.Contains("SSL") || type.Name.Contains("Certificate"))
                        {
                            Debug.Log($"[UnsafeSecurityCertificate] Found type: {type.FullName}");
                            
                            // static 필드나 프로퍼티 찾기
                            var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            foreach (var field in fields)
                            {
                                if (field.Name.ToLower().Contains("verify") || 
                                    field.Name.ToLower().Contains("validation") ||
                                    field.Name.ToLower().Contains("check"))
                                {
                                    Debug.Log($"  - Field: {field.Name} ({field.FieldType})");
                                    if (field.FieldType == typeof(bool))
                                    {
                                        field.SetValue(null, false);
                                        Debug.Log($"    Set to false");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnsafeSecurityCertificate] Unity 내부 설정 실패: {e.Message}");
        }
    }

    /// <summary>
    /// CertificateHandler 구현 (Unity 2018.1+)
    /// </summary>
    public class BypassCertificate : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            Debug.Log("[BypassCertificate] SSL 인증서 검증 우회 (모든 인증서 신뢰)");
            return true;
        }
    }
}

/// <summary>
/// 대체 방법: HTTP를 사용하는 래퍼
/// </summary>
public class HTTPFallback
{
    /// <summary>
    /// HTTPS 대신 HTTP 사용
    /// </summary>
    public static string ConvertToHTTP(string httpsUrl)
    {
        if (httpsUrl.StartsWith("https://"))
        {
            string httpUrl = httpsUrl.Replace("https://", "http://");
            Debug.LogWarning($"[HTTPFallback] HTTPS를 HTTP로 변경: {httpUrl}");
            Debug.LogWarning("경고: HTTP는 보안되지 않은 연결입니다!");
            return httpUrl;
        }
        return httpsUrl;
    }
}

/// <summary>
/// Unity Editor 전용 설정
/// </summary>
#if UNITY_EDITOR
public static class EditorSSLFix
{
    [UnityEditor.InitializeOnLoadMethod]
    static void Initialize()
    {
        Debug.Log("[EditorSSLFix] Unity Editor SSL 설정 초기화");
        
        // Editor 설정
        var editorPrefs = typeof(UnityEditor.EditorPrefs);
        var method = editorPrefs.GetMethod("SetBool", BindingFlags.Static | BindingFlags.Public);
        if (method != null)
        {
            try
            {
                method.Invoke(null, new object[] { "kSSLVerify", false });
                Debug.Log("[EditorSSLFix] Editor SSL 검증 비활성화 설정");
            }
            catch { }
        }
        
        // 전역 설정
        UnsafeSecurityCertificate.SetupGlobalSSL();
    }
}
#endif