using UnityEngine;
using UnityEngine.Rendering;           // AsyncGPUReadback
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;


// 기본 파일 저장 위치: C:\Users\RYG\AppData\LocalLow\DefaultCompany\ParaSim2025

public class FFMPEGRecorder : MonoBehaviour
{
    public static FFMPEGRecorder Inst { get; private set; }
    
    [Header("Target & Output")]
    public Camera targetCamera;          // 녹화할 카메라
    public int width  = 1280;
    public int height = 720;
    public int fps    = 30;
    public string outFile;

    RenderTexture _rt;
    Process       _ffmpeg;
    Stream        _stdin;
    bool          _recording;
    Queue<AsyncGPUReadbackRequest> _queue = new();
    
    private void Awake()
    {
        if (Inst == null)
        {
            Inst = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ---------- API ----------
    public void StartRecording()
    {
        
        if (_recording) return;
        _stopRequested = false;
        
        // 1) 경로 설정 ──────────────────────────────────────────────
        string outputDir =
#if UNITY_EDITOR // 에디터 실행
        Path.Combine(Application.dataPath, "StreamingAssets", "RecordVideos");
#else // 빌드 실행
        Path.Combine(Application.streamingAssetsPath, "RecordVideos");
#endif
        // 1) 녹화용 RenderTexture
        // _rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        // targetCamera.targetTexture = _rt;
        _rt = targetCamera.targetTexture;

        // 2) FFmpeg 실행경로
        string ffmpegPath =
#if UNITY_STANDALONE_WIN
            Path.Combine(Application.streamingAssetsPath, "ffmpeg/windows/ffmpeg.exe");
#elif UNITY_STANDALONE_OSX
            Path.Combine(Application.streamingAssetsPath, "ffmpeg/osx/ffmpeg");
#else
            Path.Combine(Application.streamingAssetsPath, "ffmpeg/linux/ffmpeg");
#endif

        // 3) 출력 파일명
        outFile = Path.Combine(outputDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
        Debug.Log("<color=cyan>[FFMPEG]</color>Out File Path: " + outFile+"</color>");
        
        // 4) FFmpeg 프로세스 시작
        _ffmpeg = new Process {
            StartInfo = new ProcessStartInfo {
                FileName  = ffmpegPath, // FFmpeg 실행 파일
                Arguments = $"-y -f rawvideo -pixel_format bgra -video_size {width}x{height} " +
                            $"-framerate {fps} -i - -vf vflip " +             // Unity 좌표계 ↔ 영상 좌표계
                            "-c:v libx264 -preset fast -pix_fmt yuv420p " +
                            $"\"{outFile}\"",
                UseShellExecute        = false,
                RedirectStandardInput  = true,
                CreateNoWindow         = true
            }
        };
        _ffmpeg.Start();
        _stdin = _ffmpeg.StandardInput.BaseStream;

        _recording = true;
        StartCoroutine(CaptureLoop());
        UnityEngine.Debug.Log($"[FFMPEG] Recording start → {outFile}");
    }

    private bool _stopRequested = false;
    
    public void StopRecording()
    {
        if (!_recording) return;
        // _recording = false;
        //
        // // targetCamera.targetTexture = null;
        //
        // _stdin.Flush();  _stdin.Close();
        // _ffmpeg.WaitForExit(); _ffmpeg.Close();
        //
        // // Destroy(_rt);
        // UnityEngine.Debug.Log("[FFMPEG] Recording finished");
        _stopRequested = true;
    }

    // ---------- 내부 루프 ----------
    IEnumerator CaptureLoop()
    {
        var eof = new WaitForEndOfFrame();
        while (_recording)
        {
            yield return eof;

            // 새 요청
            if (!_stopRequested)
            {
                var req = AsyncGPUReadback.Request(_rt, 0);
                _queue.Enqueue(req);
            }

            // 완료된 버퍼 처리
            while (_queue.Count > 0 && _queue.Peek().done)
            {
                var r = _queue.Dequeue();
                if (!r.hasError) _stdin.Write(r.GetData<byte>().ToArray());
            }

            // 모두 전송했으면 clean-up 후 종료
            if (_stopRequested && _queue.Count == 0)
                break;
        }
        
        // 여기서 안전하게 정리
        _stdin.Flush();  
        _stdin.Close();
        _ffmpeg.WaitForExit();  
        _ffmpeg.Close();
        // Destroy(_rt);

        _recording = false;
        Debug.Log("[FFMPEG] Recording finished");
        
        WebSocketManager.Inst.UploadVideo(outFile);
    }

    // 테스트용 단축키
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9)) StartRecording();
        if (Input.GetKeyDown(KeyCode.F10)) StopRecording();
    }
}