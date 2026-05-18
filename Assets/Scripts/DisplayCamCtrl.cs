using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Animations;

[Serializable]
public class DisplayCamPreset
{
    public string stepName;
    public Vector3 pos;
    public Vector3 rot;
}

public class DisplayCamCtrl : MonoBehaviour
{
    [SerializeField] private Transform displayCam;
    [SerializeField] private List<DisplayCamPreset> presetList;
    
    public void SetPosition(string step)
    {
        foreach (var preset in presetList)
        {
            if (preset.stepName == step)
            {
                displayCam.localPosition = preset.pos;
                displayCam.localEulerAngles = preset.rot;
            }
        }
    }
}
