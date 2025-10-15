using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// 패스 설정 이넘 타입
/// </summary>
public enum FilePath
{
    [Description("UiData/")]
    UiData,
    [Description("Csvs/")]
    CSV,
}

/// <summary>
/// Csv 파일명을 정의
/// </summary>
public enum  DataName
{
    CD_TimeLine,
    CD_Procedure,
    CD_Instruction,
    CD_Evaluation,
    CD_Fogs,
    CD_Weather,
    CD_Routes,
    CD_SimInfo,
    CD_Contingency
}

public static class FileUtils
{
    /// <summary>
    /// 파일이름을 제외한 폴더 경로 불러오기
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetFilePath(FilePath path)
    {
        // Application.streamingAssetsPath와 FilePath의 설명을 이용해 폴더 경로를 생성합니다.
        string folderPath = Path.Combine(Application.streamingAssetsPath, path.ToDescription());

        // 폴더가 존재하지 않으면 생성합니다.
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log($"Created directory: {folderPath}");
        }

        return folderPath;
    }
    
    /// <summary>
    /// 해당 파일 이름으로 이미지를 불러와서 Sprite로 변환 후 반환
    /// </summary>
    /// <param name="sprName">이미지 이름</param>
    /// <returns></returns>
    public static Sprite GetSprite(string sprName, FilePath path)
    {
        // 파일 경로를 생성
        var filePath = Path.Combine(GetFilePath(path), sprName + ".png");
        Debug.Log("[NfUtils] Sprite filePath: " + filePath);

        // 파일이 존재하는지 확인
        if (File.Exists(filePath))
        {
            // 파일의 바이트 데이터를 읽음
            var bytes = File.ReadAllBytes(filePath);

            // 텍스처를 생성하고 이미지를 로드
            var texture = new Texture2D(0, 0);
            texture.LoadImage(bytes);

            // 스프라이트 기본 정보 세팅 후 생성 및 이름 정하기
            Rect rectSize = new Rect(0, 0, texture.width, texture.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            Sprite sprite = Sprite.Create(texture, rectSize, pivot);
            sprite.name = sprName;

            // 스프라이트를 생성하여 반환
            return sprite;
        }

        // 파일이 없는 경우 null 반환
        return null;
    }

    /// <summary>
    /// Resources 폴더에서 음성 파일을 로드하여 재생
    /// instruction의 voiceContent 컬럼에 정의된 이름과 동일한 음원을 재생
    /// 음성 파일은 Assets/Resources/Voices/ 폴더에 위치해야 함
    /// </summary>
    /// <param name="voiceName">음성 파일 이름 (확장자 제외)</param>
    /// <returns>재생할 오디오</returns>
    public static AudioClip GetVoice(string voiceName)
    {
        var voiceStr = voiceName + ".mp3";

        // Resources 폴더에서 AudioClip 로드
        // Resources.Load는 Assets/Resources/ 폴더를 기준으로 함
        string resourcePath = "Voices/" + voiceStr;
        AudioClip audioClip = Resources.Load<AudioClip>(resourcePath);

        if (audioClip != null)
        {
            Debug.Log($"[NfUtils] Successfully playing audio: {voiceStr}");
            return audioClip;
        }
        else
        {
            Debug.LogWarning($"[NfUtils] Audio file not found in Resources: {resourcePath}");
            return null;
        }
    }
    
    
    public static string ToDescription(this Enum descType)
    {
        var fi = descType.GetType().GetField(descType.ToString());
        var att = (DescriptionAttribute)fi.GetCustomAttribute(typeof(DescriptionAttribute));
        return att != null ? att.Description : descType.ToString();
    }
}

public class UIUtils
{
    /// <summary>
    /// EaseOutBack 곡선 (뒤로 약간 튕겼다 나오듯이 커지는 느낌)
    /// 주로 등장 애니메이션에서 사용
    /// </summary>
    /// <param name="t">0 ~ 1 사이의 진행값 (시간 비율)</param>
    /// <returns>보간된 비율</returns>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;

        // t가 1에 가까워질수록 빠르게 접근 + 약간 초과한 후 되돌아옴 (overshoot)
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    /// <summary>
    /// EaseOutQuad 곡선 (처음 빠르고 끝으로 갈수록 느려지는 속도)
    /// 자연스러운 감속 애니메이션에 적합
    /// </summary>
    public static float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t); // 역으로 생각: 처음 속도 빠르게 시작
    }

    /// <summary>
    /// EaseInQuad 곡선 (처음 느리게 시작해서 가속하는 느낌)
    /// 자연스러운 페이드 아웃, 퇴장 애니메이션 등에 적합
    /// </summary>
    public static float EaseInQuad(float t)
    {
        return t * t; // 곡선이 아래로 움푹 들어간 형태
    }
}


public class Vector3S
{
    public short x;
    public short y;
    public short z;

    public Vector3S(short pX, short pY, short pZ)
    {
        x = pX;
        y = pY;
        z = pZ;
    }
}

public class Vector2S
{
    public short y;
    public short z;
    
    public Vector2S(short pY, short pZ)
    {
        y = pY;
        z = pZ;
    }
}
