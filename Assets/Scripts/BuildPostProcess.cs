#if UNITY_EDITOR
using UnityEditor; 
using UnityEditor.Callbacks; 
using System.IO; 
using UnityEngine;

public class BuildPostProcess
{
    [PostProcessBuild]
    static void SetExec(BuildTarget target, string path)
    {
        if (target == BuildTarget.StandaloneOSX)
        {
            var ff = Path.Combine(path,
                "Contents/Resources/Data/StreamingAssets/ffmpeg/windows/ffmpeg");
            System.Diagnostics.Process.Start("chmod", $"+x \"{ff}\"");
        }
    }
}
#endif