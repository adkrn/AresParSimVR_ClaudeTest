using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public enum WeatherType
{
   Custom,
   ClearDay,
   ClearNight,
   CloudyDay,
   CloudyNight,
   RainyDay,
   RainyNight,
}

public enum FogDensity
{
   None = 0,
   Barely = 5000,
   Light = 3000,
   Medium = 1500,
   Heavy = 500
}

public class WeatherManager : MonoBehaviour
{
   public WeatherType weatherType = WeatherType.Custom; 
   [Header("현재 시각")]
   [Range(0,24)]
   public float sceneTime = 14f;
   public float sunIntensityMulti = 3f;
   
   public Transform sunLightT;
   public Light sunLight;
   public AnimationCurve sunIntensityC;
   public Gradient sunColorG;
   public Gradient skyColorG;
   public Gradient horizonColorG;

   public Material[] cloudMat;
   [Header("비 파티클")] 
   public GameObject rainParticle; 
   
   [Header("비 온/오프")]
   public bool isRain = false;
   
   [Header("구름 양")]
   public float cloudiness = 0.5f;
   [Header("구름 밀도")]
   public float cloudDensity = 0.5f;
   [Header("구름 높이")]
   public float cloudHeight = 0.5f;
   [Header("구름 파티클 불투명도")]
   public float cloudOpacity = 0.5f;

   [Header("안개 밀도")]
   public FogDensity fogDensity = FogDensity.Barely;
   public float fogEndDistance = 10000f; // FogDensity.None 일 경우 fogEndDistance가 적용된다.
   // 실제 안개 거리는 fogEndDistance와 fogDensity를 참조하여 이 변수에 값을 대입후 최종 렌더세팅이 적용하는 방식
   private float fogDensityVal;

   [Header("하늘색->안개색 완전전환 거리(m)")]
   public float fogSkyColorMixMax =2000f;
   [Header("안개 밝기 재조정"),SerializeField]
   private float fogLightnessVal = 0.8f;
   
   public List<WeatherTypeInfo> weatherList = new List<WeatherTypeInfo>();

   public WeatherTypeInfo selWeatherInfo;
   
   private bool isPlaying = false;
   private void Start()
   {
      isPlaying = true;
      
      // 시나리오 데이터를 참조해서 가져온 날씨, 안개 데이터를 활용해 환경을 설정한다.
      weatherType = DataManager.Inst.GetWeatherData().weatherType;
      fogDensity = DataManager.Inst.GetFogData().type;
      
      
      Debug.Log("현재 날씨 : " + weatherType.ToString() + " 현재 안개 : " + fogDensity.ToString());
      Init(weatherType, fogDensity);
   }

   private void OnDrawGizmos()
   {
      if (isPlaying) return;
      Init(weatherType, fogDensity);
   }

   private void SetWeather(WeatherTypeInfo weatherInfo = null)
   {
      var timeValue = weatherInfo.sceneTime / 24f;

      var skyColorOrigin = skyColorG.Evaluate(timeValue);
      var fogColorOrigin = horizonColorG.Evaluate(timeValue);
      var fogMixVal = 1 - (fogSkyColorMixMax / fogDensityVal);
      var skyColorMixFog = Color.Lerp(fogColorOrigin, skyColorOrigin,fogMixVal);
      RenderSettings.skybox.SetVector("_SkyColor", skyColorMixFog);
      
      sunLightT.eulerAngles = new Vector3(0, 0,(weatherInfo.sceneTime / 24f) * 360f);
      sunLight.color = sunColorG.Evaluate(timeValue);
      sunLight.intensity = sunIntensityC.Evaluate(timeValue) *weatherInfo.sunIntensityMulti *fogMixVal;
      
      RenderSettings.skybox.SetVector("_HorizonColor", horizonColorG.Evaluate(timeValue));
      RenderSettings.skybox.SetVector("_SunColor", sunColorG.Evaluate(timeValue));
      RenderSettings.skybox.SetFloat("_SunElevation", ((weatherInfo.sceneTime-6) / 24f) * 360f);
      RenderSettings.skybox.SetFloat("_CloudColorPower", (1-sunLight.intensity));
      
      var cloudLightingParams = RenderSettings.skybox.GetVector("_FlatCloudsLightingParams");
      cloudLightingParams.x = sunIntensityC.Evaluate(timeValue) + 0.25f;
      cloudLightingParams.y = sunIntensityC.Evaluate(timeValue) + 0.1f;
      RenderSettings.skybox.SetVector("_FlatCloudsLightingParams",cloudLightingParams);
      
      var cloudParams = RenderSettings.skybox.GetVector("_FlatCloudsParams");
      cloudParams.x = weatherInfo.cloudiness;
      cloudParams.y = weatherInfo.cloudDensity;
      cloudParams.z = weatherInfo.cloudHeight;
      RenderSettings.skybox.SetVector("_FlatCloudsParams",cloudParams);
      foreach (var mat in cloudMat)
      {
         var c = mat.GetColor("_Color");
         c.a = weatherInfo.cloudiness * weatherInfo.cloudOpacity;
         mat.SetColor("_Color", c);
      }

      rainParticle.SetActive(weatherInfo.isRain);
      
      RenderSettings.fogColor = horizonColorG.Evaluate(timeValue) * 0.8f;
      RenderSettings.fogEndDistance = fogDensityVal;
   }

   public void Init(WeatherType typeWeather = WeatherType.Custom,FogDensity typeFog = FogDensity.None )
   {
      weatherType = typeWeather;
      fogDensityVal = InitFog(typeFog);
      
      switch (typeWeather)
      {
         case WeatherType.ClearDay:
            ClearDay();
            break;
         
         case WeatherType.ClearNight:
            ClearNight();
            break;
         
         case WeatherType.CloudyDay:
            CloudyDay();
            break;
         
         case WeatherType.CloudyNight:
            CloudyNight();
            break;
         
         case WeatherType.RainyDay:
            RainyDay();
            break;
         
         case WeatherType.RainyNight:
            RainyNight();
            break;

         case WeatherType.Custom:
            Custom();
            break;
         default:
            break; 
      } 
   }

   public float InitFog(FogDensity type = FogDensity.None)
   {
      fogDensity = type;
      if (type == FogDensity.None) return fogEndDistance;
      return (float)type;
   }

   // 기본 날씨 변경 정보
   // 시간 > sceneTime
   // 햇빛 강도 >  sunIntensityMulti
   // 구름 양 > cloudiness
   // 구름 밀도 > cloudDensity
   // 구름 높이 > cloudHeight
   // 구름 파티클 불투명도 > cloudOpacity

   private void ClearDay()
   {      
      if (isPlaying) Debug.Log($"[WeatherManager]{weatherType} ::: 화창한 낮시간을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.ClearDay);
      SetWeather(selWeatherInfo);
   }
   
   private void ClearNight()
   {      
      if (isPlaying) Debug.Log($"[WeatherManager]{weatherType} ::: 화창한 낮시간을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.ClearNight);
      SetWeather(selWeatherInfo);
   }
   
   private void CloudyDay()
   {
      if (isPlaying)  Debug.Log($"[WeatherManager]{weatherType} ::: 구름 낀 낮을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.CloudyDay);
      SetWeather(selWeatherInfo);
   }
   
   private void CloudyNight()
   {
      if (isPlaying) Debug.Log($"[WeatherManager]{weatherType} ::: 구름 낀 저녁을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.CloudyNight);
      SetWeather(selWeatherInfo);
   }
   
   private void RainyDay()
   {
      if (isPlaying) Debug.Log($"[WeatherManager]{weatherType} ::: 비오는 낮을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.RainyDay);
      SetWeather(selWeatherInfo);
   }
   
   private void RainyNight()
   {
      if (isPlaying) Debug.Log($"[WeatherManager]{weatherType} ::: 비오는 저녁을 시뮬레이션한다.");

      selWeatherInfo =  weatherList.FirstOrDefault(x => x.weatherType == WeatherType.RainyNight);
      SetWeather(selWeatherInfo);
   }
   
   private void Custom()
   {
      selWeatherInfo = new WeatherTypeInfo
      {
         sceneTime = sceneTime,
         sunIntensityMulti = sunIntensityMulti,
         cloudiness = cloudiness,
         cloudDensity = cloudDensity,
         cloudOpacity = cloudOpacity,
         isRain = isRain,
      };
      SetWeather(selWeatherInfo);
   }
   
   // 바람 최대 최소 범위 0-100
   private int windNorth = 0;
   private int windEast = 0;
   private int windSouth = 0;
   private int windWest = 0;
}

[System.Serializable]   
public class WeatherTypeInfo
{
   public WeatherType weatherType;
   public float sceneTime = 21f;
   public float sunIntensityMulti = 1f;
   public float cloudiness = 10f;
   public float cloudDensity = 0.05f;
   public float cloudHeight = 20f;
   public float cloudOpacity = 0.005f;
   public bool isRain = true;
}

public enum ParaEvent
{
   None = 0, // 기본값: 기능없음
   FreeFall = 1, // 낙하: Pitching, Heave
   Deploy = 2, // 낙하산 산개: Pitching, Heave 
   MalFunc = 3, // 낙하산 고장: Heave
   Landing = 4, // 착륙 직전:  Heave
   Landed = 5 // 착륙: Heave
}