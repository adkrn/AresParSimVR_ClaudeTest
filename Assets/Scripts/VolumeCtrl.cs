using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using Vignette = UnityEngine.Rendering.Universal.Vignette;

[Serializable]
public class EnviPreset
{
    public string stepName;
    // ColorAdjustments
    public float postExposure;
    // camera
    public int cameraFar;
    // vignette
    public float intensity;
    public float smoothness;
}

public class VolumeCtrl : MonoBehaviour
{
    [SerializeField] private Volume volume;
    [SerializeField] private Camera mainCam;
    [SerializeField] private EnviPreset _initPreset;
    [SerializeField] private List<EnviPreset> presetList;
    [SerializeField] private float smoothTime = 2f;

    private ColorAdjustments colorAdjustments;
    private Vignette vignette;
    private EnviPreset targetPreset;
    private float exposureVelocity;
    private float farVelocity;
    private float intensityVelocity;
    private float smoothnessVelocity;

    private void Start()
    {
        volume = volume == null ? FindAnyObjectByType<Volume>() : volume;
        mainCam = mainCam == null ? Camera.main : mainCam;
        
        InitPreset();
    }

    private void InitPreset()
    {
        // 현재 color Adjustments의 postExposure를 저장한다.
        if (volume == null || mainCam == null)
        {
            Debug.LogWarning("[VolumeCtrl] Volume or MainCamera is null");
            return;
        }

        _initPreset = new EnviPreset
        {
            stepName = "Initial",
            postExposure = 0f,
            cameraFar = (int)mainCam.farClipPlane
        };
        
        if (volume.profile != null && volume.profile.TryGet<ColorAdjustments>(out colorAdjustments))
        {
            _initPreset.postExposure = colorAdjustments.postExposure.value;
        }
        else
        {
            Debug.LogWarning("[VolumeCtrl] ColorAdjustments settings not found in volume profile");
        }
        
        if (volume.profile != null && volume.profile.TryGet<Vignette>(out vignette))
        {
            _initPreset.intensity = vignette.intensity.value;
            _initPreset.smoothness = vignette.smoothness.value;
        }
        else
        {
            Debug.LogWarning("[VolumeCtrl] ColorAdjustments settings not found in volume profile");
        }

        Debug.Log($"[VolumeCtrl] Initial preset saved");
        smoothTime = 2;
    }

    public void ApplyPreset(string stepName, float customSmooth = 2)
    {
        targetPreset = presetList.Find(p => p.stepName == stepName);
        if (targetPreset == null)
        {
            Debug.LogWarning($"[VolumeCtrl] Preset not found: {stepName}");
        }

        smoothTime = customSmooth;
    }

    private void Update()
    {
        if (targetPreset == null || colorAdjustments == null) return;

        colorAdjustments.postExposure.value = Mathf.SmoothDamp(
            colorAdjustments.postExposure.value,
            targetPreset.postExposure,
            ref exposureVelocity,
            smoothTime
        );

        mainCam.farClipPlane = Mathf.SmoothDamp(
            mainCam.farClipPlane,
            targetPreset.cameraFar,
            ref farVelocity,
            smoothTime
        );

        vignette.intensity.value = Mathf.SmoothDamp(
            vignette.intensity.value,
            targetPreset.intensity,
            ref intensityVelocity,
            smoothTime
        );
        
        vignette.smoothness.value = Mathf.SmoothDamp(
            vignette.smoothness.value,
            targetPreset.smoothness,
            ref smoothnessVelocity,
            smoothTime
        );
    }
}
