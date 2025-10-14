// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Custom/Skybox/Procedural" {
Properties {
    [KeywordEnum(None, Simple, High Quality)] _SunDisk ("Sun", Int) = 2
    _SunSize ("Sun Size", Range(0,1)) = 0.04
    _SunSizeConvergence("Sun Size Convergence", Range(1,10)) = 5

    _AtmosphereThickness ("Atmosphere Thickness", Range(0,5)) = 1.0
    _SkyTint ("Sky Tint", Color) = (.5, .5, .5, 1)
    _GroundColor ("Ground", Color) = (.369, .349, .341, 1)

    _Exposure("Exposure", Range(0, 8)) = 1.3
    
    // ─ Flat (평형운) ─────────────────────────────────
    _FlatCloudsBaseTexture   ("Flat Base Noise",   2D) = "white" {}
    _FlatCloudsDetailTexture ("Flat Detail Noise", 2D) = "white" {}
    _FlatCloudsAnimation     ("Flat Scroll (XY DetailXY)", Vector) = (0.01,0.005,0.02,0.01)
    
    _FlatCloudsLightDirection("Flat Light Dir", Vector) = (0.3,0.8,0.2,0)
	_FlatCloudsLightColor    ("Flat Light Color", Color) = (1,0.97,0.9,1)
	_FlatCloudsAmbientColor  ("Flat Ambient",     Color) = (0.6,0.65,0.7,1)

	_FlatCloudsLightingParams("Flat Lighting (L,A,Abs,Hg)", Vector) = (1,0.4,0.3,0.2)
	_FlatCloudsParams        ("Flat Params (Cov,Dens,Alt,Tone)", Vector) = (0.7,1,0.15,0)
	_FlatCloudsTiling        ("Flat Tiling (Base,Detail)", Vector) = (0.25,0.5,0,0)
}

SubShader {
    Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
    Cull Off ZWrite Off

    Pass {

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag

        #include "UnityCG.cginc"
        #include "Lighting.cginc"

        #pragma multi_compile_local _SUNDISK_NONE _SUNDISK_SIMPLE _SUNDISK_HIGH_QUALITY

        uniform half _Exposure;     // HDR exposure
        uniform half4 _GroundColor;
        uniform half _SunSize;
        uniform half _SunSizeConvergence;
        uniform half4 _SkyTint;
        uniform half _AtmosphereThickness;

     	uniform sampler2D _FlatCloudsBaseTexture;
		uniform sampler2D _FlatCloudsDetailTexture;

        uniform float4 _FlatCloudsAnimation;
		uniform float3 _FlatCloudsLightDirection;
		uniform float3 _FlatCloudsLightColor;
		uniform float3 _FlatCloudsAmbientColor;
		uniform float4 _FlatCloudsLightingParams; // x = LightIntensity, y = AmbientIntensity, z = Absorbtion, w = HgPhase
 		uniform float4 _FlatCloudsParams; // x = Coverage, y = Density, z = Altitude, w = tonemapping
        uniform float4 _FlatCloudsTiling; // x = Base, y = Detail
		uniform float _CloudsExposure;
        


    #if defined(UNITY_COLORSPACE_GAMMA)
        #define GAMMA 2
        #define COLOR_2_GAMMA(color) color
        #define COLOR_2_LINEAR(color) color*color
        #define LINEAR_2_OUTPUT(color) sqrt(color)
    #else
        #define GAMMA 2.2
        // HACK: to get gfx-tests in Gamma mode to agree until UNITY_ACTIVE_COLORSPACE_IS_GAMMA is working properly
        #define COLOR_2_GAMMA(color) ((unity_ColorSpaceDouble.r>2.0) ? pow(color,1.0/GAMMA) : color)
        #define COLOR_2_LINEAR(color) color
        #define LINEAR_2_LINEAR(color) color
    #endif

        // RGB wavelengths
        // .35 (.62=158), .43 (.68=174), .525 (.75=190)
        static const float4 kDefaultScatteringWavelength = float4(.65, .57, .475, 1.0);
        static const float4 kVariableRangeForScatteringWavelength = float4(.15, .15, .15, 1.0);

        #define OUTER_RADIUS 1.025
        static const float kOuterRadius = OUTER_RADIUS;
        static const float kOuterRadius2 = OUTER_RADIUS*OUTER_RADIUS;
        static const float kInnerRadius = 1.0;
        static const float kInnerRadius2 = 1.0;

        static const float kCameraHeight = 0.0001;

        #define kRAYLEIGH (lerp(0.0, 0.0025, pow(_AtmosphereThickness,2.5)))      // Rayleigh constant
        #define kMIE 0.0010             // Mie constant
        #define kSUN_BRIGHTNESS 20.0    // Sun brightness

        #define kMAX_SCATTER 50.0 // Maximum scattering value, to prevent math overflows on Adrenos

        static const float kHDSundiskIntensityFactor = 15.0;
        static const float kSimpleSundiskIntensityFactor = 27.0;

        static const half kSunScale = 400.0 * kSUN_BRIGHTNESS;
        static const float kKmESun = kMIE * kSUN_BRIGHTNESS;
        static const float kKm4PI = kMIE * 4.0 * 3.14159265;
        static const float kScale = 1.0 / (OUTER_RADIUS - 1.0);
        static const float kScaleDepth = 0.25;
        static const float kScaleOverScaleDepth = (1.0 / (OUTER_RADIUS - 1.0)) / 0.25;
        static const float kSamples = 2.0; // THIS IS UNROLLED MANUALLY, DON'T TOUCH

        #define MIE_G (-0.990)
        #define MIE_G2 0.9801

        #define SKY_GROUND_THRESHOLD 0.02

        // fine tuning of performance. You can override defines here if you want some specific setup
        // or keep as is and allow later code to set it according to target api

        // if set vprog will output color in final color space (instead of linear always)
        // in case of rendering in gamma mode that means that we will do lerps in gamma mode too, so there will be tiny difference around horizon
        // #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 0

        // sun disk rendering:
        // no sun disk - the fastest option
        #define SKYBOX_SUNDISK_NONE 0
        // simplistic sun disk - without mie phase function
        #define SKYBOX_SUNDISK_SIMPLE 1
        // full calculation - uses mie phase function
        #define SKYBOX_SUNDISK_HQ 2

        // uncomment this line and change SKYBOX_SUNDISK_SIMPLE to override material settings
        // #define SKYBOX_SUNDISK SKYBOX_SUNDISK_SIMPLE

    #ifndef SKYBOX_SUNDISK
        #if defined(_SUNDISK_NONE)
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_NONE
        #elif defined(_SUNDISK_SIMPLE)
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_SIMPLE
        #else
            #define SKYBOX_SUNDISK SKYBOX_SUNDISK_HQ
        #endif
    #endif

    #ifndef SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
        #if defined(SHADER_API_MOBILE)
            #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 1
        #else
            #define SKYBOX_COLOR_IN_TARGET_COLOR_SPACE 0
        #endif
    #endif

        // Calculates the Rayleigh phase function
        half getRayleighPhase(half eyeCos2)
        {
            return 0.75 + 0.75*eyeCos2;
        }
        half getRayleighPhase(half3 light, half3 ray)
        {
            half eyeCos = dot(light, ray);
            return getRayleighPhase(eyeCos * eyeCos);
        }

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VertexInput 
        {
            float4 vertex : POSITION;
            float3 texcoord : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        
        struct v2f
        {
            float4 pos : SV_POSITION;

        #if   SKYBOX_SUNDISK == SKYBOX_SUNDISK_HQ      // _SUNDISK_HIGH_QUALITY
            float3 vertex  : TEXCOORD0;    // 하늘빛 계산용 법선
        #elif SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE  // _SUNDISK_SIMPLE
            float3 rayDir  : TEXCOORD0;    // 단순 모드용 광선
        #else                                          // _SUNDISK_NONE
            float uv : TEXCOORD0;    // 지평선 lerp 파라미터
        #endif

            half4 skyColor    : TEXCOORD1;
            half4 groundColor : TEXCOORD2;

        #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE      // 해 디스크가 있을 때만
            half4 sunColor    : TEXCOORD3;
        #endif

            UNITY_FOG_COORDS(4)            // ← 5번으로 확정
            UNITY_VERTEX_OUTPUT_STEREO
        };


        float scale(float inCos)
        {
            float x = 1.0 - inCos;
            return 0.25 * exp(-0.00287 + x*(0.459 + x*(3.83 + x*(-6.80 + x*5.25))));
        }

        v2f vert (appdata v)
        {
            v2f OUT;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
            OUT.pos = UnityObjectToClipPos(v.vertex);

            float4 kSkyTintInGammaSpace = COLOR_2_GAMMA(_SkyTint); // convert tint from Linear back to Gamma
            float4 kScatteringWavelength = lerp (
                kDefaultScatteringWavelength-kVariableRangeForScatteringWavelength,
                kDefaultScatteringWavelength+kVariableRangeForScatteringWavelength,
                half4(1,1,1,1) - kSkyTintInGammaSpace); // using Tint in sRGB gamma allows for more visually linear interpolation and to keep (.5) at (128, gray in sRGB) point
            float4 kInvWavelength = 1.0 / pow(kScatteringWavelength, 4);

            float kKrESun = kRAYLEIGH * kSUN_BRIGHTNESS;
            float kKr4PI = kRAYLEIGH * 4.0 * 3.14159265;

            float4 cameraPos = float4(0,kInnerRadius + kCameraHeight,0,0);    // The camera's current position

            // Get the ray from the camera to the vertex and its length (which is the far point of the ray passing through the atmosphere)
            float4 eyeRay = normalize(mul((float4x4)unity_ObjectToWorld, v.vertex.xyz));

            float far = 0.0;
            half4 cIn, cOut;

            if(eyeRay.y >= 0.0)
            {
                // Sky
                // Calculate the length of the "atmosphere"
                far = sqrt(kOuterRadius2 + kInnerRadius2 * eyeRay.y * eyeRay.y - kInnerRadius2) - kInnerRadius * eyeRay.y;

                float3 pos = cameraPos + far * eyeRay;

                // Calculate the ray's starting position, then calculate its scattering offset
                float height = kInnerRadius + kCameraHeight;
                float depth = exp(kScaleOverScaleDepth * (-kCameraHeight));
                float startAngle = dot(eyeRay, cameraPos) / height;
                float startOffset = depth*scale(startAngle);


                // Initialize the scattering loop variables
                float sampleLength = far / kSamples;
                float scaledLength = sampleLength * kScale;
                float3 sampleRay = eyeRay * sampleLength;
                float3 samplePoint = cameraPos + sampleRay * 0.5;

                // Now loop through the sample rays
                float4 frontColor = 0;
                // Weird workaround: WP8 and desktop FL_9_3 do not like the for loop here
                // (but an almost identical loop is perfectly fine in the ground calculations below)
                // Just unrolling this manually seems to make everything fine again.
//              for(int i=0; i<int(kSamples); i++)
                {
                    float height = length(samplePoint);
                    float depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
                    float lightAngle = dot(_WorldSpaceLightPos0.xyz, samplePoint) / height;
                    float cameraAngle = dot(eyeRay, samplePoint) / height;
                    float scatter = (startOffset + depth*(scale(lightAngle) - scale(cameraAngle)));
                    float4 attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));

                    frontColor += attenuate * (depth * scaledLength);
                    samplePoint += sampleRay;
                }
                {
                    float height = length(samplePoint);
                    float depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
                    float lightAngle = dot(_WorldSpaceLightPos0.xyz, samplePoint) / height;
                    float cameraAngle = dot(eyeRay, samplePoint) / height;
                    float scatter = (startOffset + depth*(scale(lightAngle) - scale(cameraAngle)));
                    float4 attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));

                    frontColor += attenuate * (depth * scaledLength);
                    samplePoint += sampleRay;
                }



                // Finally, scale the Mie and Rayleigh colors and set up the varying variables for the pixel shader
                cIn = frontColor * (kInvWavelength * kKrESun);
                cOut = frontColor * kKmESun;
            }
            else
            {
                // Ground
                far = (-kCameraHeight) / (min(-0.001, eyeRay.y));

                float3 pos = cameraPos + far * eyeRay;

                // Calculate the ray's starting position, then calculate its scattering offset
                float depth = exp((-kCameraHeight) * (1.0/kScaleDepth));
                float cameraAngle = dot(-eyeRay, pos);
                float lightAngle = dot(_WorldSpaceLightPos0.xyz, pos);
                float cameraScale = scale(cameraAngle);
                float lightScale = scale(lightAngle);
                float cameraOffset = depth*cameraScale;
                float temp = (lightScale + cameraScale);

                // Initialize the scattering loop variables
                float sampleLength = far / kSamples;
                float scaledLength = sampleLength * kScale;
                float4 sampleRay = eyeRay * sampleLength;
                float4 samplePoint = cameraPos + sampleRay * 0.5;

                // Now loop through the sample rays
                float4 frontColor = float4(0.0, 0.0, 0.0,1.0);
                float4 attenuate;
//              for(int i=0; i<int(kSamples); i++) // Loop removed because we kept hitting SM2.0 temp variable limits. Doesn't affect the image too much.
                {
                    float height = length(samplePoint);
                    float depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
                    float scatter = depth*temp - cameraOffset;
                    attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));
                    frontColor += attenuate * (depth * scaledLength);
                    samplePoint += sampleRay;
                }

                cIn = frontColor * (kInvWavelength * kKrESun + kKmESun);
                cOut = clamp(attenuate, 0.0, 1.0);
            }

        #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_HQ
            OUT.vertex          = -eyeRay;
        #elif SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
            OUT.rayDir          = half3(-eyeRay);
        #else
            OUT.uv = -eyeRay.y / SKY_GROUND_THRESHOLD;
        #endif

            // if we want to calculate color in vprog:
            // 1. in case of linear: multiply by _Exposure in here (even in case of lerp it will be common multiplier, so we can skip mul in fshader)
            // 2. in case of gamma and SKYBOX_COLOR_IN_TARGET_COLOR_SPACE: do sqrt right away instead of doing that in fshader

            OUT.groundColor = _Exposure * (cIn + COLOR_2_LINEAR(_GroundColor) * cOut);
            OUT.skyColor    = _Exposure * (cIn * getRayleighPhase(_WorldSpaceLightPos0.xyz, -eyeRay));

        #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
            // The sun should have a stable intensity in its course in the sky. Moreover it should match the highlight of a purely specular material.
            // This matching was done using the standard shader BRDF1 on the 5/31/2017
            // Finally we want the sun to be always bright even in LDR thus the normalization of the lightColor for low intensity.
            half lightColorIntensity = clamp(length(_LightColor0.xyz), 0.25, 1);
            #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
                OUT.sunColor    = kSimpleSundiskIntensityFactor * saturate(cOut * kSunScale) * _LightColor0.xyzw / lightColorIntensity;
            #else // SKYBOX_SUNDISK_HQ
                OUT.sunColor    = kHDSundiskIntensityFactor * saturate(cOut) * _LightColor0.xyzw / lightColorIntensity;
            #endif

        #endif

        #if defined(UNITY_COLORSPACE_GAMMA) && SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
            OUT.groundColor = sqrt(OUT.groundColor);
            OUT.skyColor    = sqrt(OUT.skyColor);
            #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
                OUT.sunColor= sqrt(OUT.sunColor);
            #endif
        #endif

            return OUT;
        }


        // Calculates the Mie phase function
        half getMiePhase(half eyeCos, half eyeCos2)
        {
            half temp = 1.0 + MIE_G2 - 2.0 * MIE_G * eyeCos;
            temp = pow(temp, pow(_SunSize,0.65) * 10);
            temp = max(temp,1.0e-4); // prevent division by zero, esp. in half precision
            temp = 1.5 * ((1.0 - MIE_G2) / (2.0 + MIE_G2)) * (1.0 + eyeCos2) / temp;
            #if defined(UNITY_COLORSPACE_GAMMA) && SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
                temp = pow(temp, .454545);
            #endif
            return temp;
        }

        // Calculates the sun shape
        half calcSunAttenuation(half3 lightPos, half3 ray)
        {
        #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
            half3 delta = lightPos - ray;
            half dist = length(delta);
            half spot = 1.0 - smoothstep(0.0, _SunSize, dist);
            return spot * spot;
        #else // SKYBOX_SUNDISK_HQ
            half focusedEyeCos = pow(saturate(dot(lightPos, ray)), _SunSizeConvergence);
            return getMiePhase(-focusedEyeCos, focusedEyeCos * focusedEyeCos);
        #endif
        }

        half3 tonemapACES(half3 color, float Exposure)
		{
			color *= Exposure;

			// See https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
			const half a = 2.51;
			const half b = 0.03;
			const half c = 2.43;
			const half d = 0.59;
			const half e = 0.14;
			return saturate((color * (a * color + b)) / (color * (c * color + d) + e));
		}

		float Remap(float org_val, float org_min, float org_max, float new_min, float new_max)
		{
			return new_min + saturate(((org_val - org_min) / (org_max - org_min))*(new_max - new_min));
		}
        
        float HenryGreenstein(float cosTheta, float g)
		{
			float k = 3.0 / (8.0 * 3.1415926f) * (1.0 - g * g) / (2.0 + g * g);
			return k * (1.0 + cosTheta * cosTheta) / pow(abs(1.0 + g * g - 2.0 * g * cosTheta), 1.5);
		}
        
        float CalculateCloudDensity(float2 posBase, float2 posDetail, float coverage)
		{
			float4 baseNoise = tex2D(_FlatCloudsBaseTexture, posBase);
			float low_freq_fBm = (baseNoise.g * 0.625) + (baseNoise.b * 0.25) + (baseNoise.a * 0.125);
			float base_cloud = Remap(baseNoise.r, -(1.0 - low_freq_fBm), 1.0, 0.0, 1.0) * coverage;

			float4 detailNoise = tex2D(_FlatCloudsDetailTexture, posDetail * 2);
			float high_freq_fBm = (detailNoise.r * 0.625) + (detailNoise.g * 0.25) + (detailNoise.b * 0.125);
			float density = Remap(base_cloud, 1.0 - high_freq_fBm * 0.5, 1.0, 0.0, 1.0);

			density *= pow(high_freq_fBm, 0.4);
			density *= _FlatCloudsParams.y;

			return density;
		}

        half4 frag (v2f i) : SV_Target
        {
            half4 col = 0;

        // if y > 1 [eyeRay.y < -SKY_GROUND_THRESHOLD] - ground
        // if y >= 0 and < 1 [eyeRay.y <= 0 and > -SKY_GROUND_THRESHOLD] - horizon
        // if y < 0 [eyeRay.y > 0] - sky
        #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_HQ
            half3 ray = normalize(i.vertex.xyz);
            half y = ray.y / SKY_GROUND_THRESHOLD;
        #elif SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
            half3 ray = i.rayDir.xyz;
            half y = ray.y / SKY_GROUND_THRESHOLD;
        #else
            half y = i.uv;
        #endif

            // if we did precalculate color in vprog: just do lerp between them
            col = lerp(i.skyColor, i.groundColor, saturate(y));

        #if SKYBOX_SUNDISK != SKYBOX_SUNDISK_NONE
            if(y < 0.0)
            {
                col += i.sunColor * calcSunAttenuation(_WorldSpaceLightPos0.xyz, -ray);
            }
        #endif

            
        #if defined(UNITY_COLORSPACE_GAMMA) && !SKYBOX_COLOR_IN_TARGET_COLOR_SPACE
            col = LINEAR_2_OUTPUT(col);
        #endif

            // 구름 추가를 위한 코드 ////////////////
            float3 uvs;
            #if SKYBOX_SUNDISK == SKYBOX_SUNDISK_HQ || SKYBOX_SUNDISK == SKYBOX_SUNDISK_SIMPLE
                uvs = ray;                      // 방향 벡터 그대로 사용
            #else
                uvs = float3(i.uv, 0, 0);      // 스칼라 → (x,0,z) 로 변환
            #endif

            float4 uv1;
            uv1.xy = (uvs.xz * _FlatCloudsTiling.x) + _FlatCloudsAnimation.xy;
            uv1.zw = (uvs.xz * _FlatCloudsTiling.y) + _FlatCloudsAnimation.zw;

			float cloudExtinction = pow(uvs.y, 2);
			half density = CalculateCloudDensity(uv1.xy, uv1.zw, _FlatCloudsParams.x);
            
            ///////////////////////////////////////
            // Lighting
            fixed absorbtion = exp2(-1 * (density * _FlatCloudsLightingParams.z));
            float3 viewDir = normalize(i.groundColor - _WorldSpaceCameraPos);
            float inscatterAngle = dot(normalize(_FlatCloudsLightDirection), -viewDir);
			fixed hg = HenryGreenstein(inscatterAngle, _FlatCloudsLightingParams.w) * 2 * absorbtion;
			fixed lighting = density * (absorbtion + hg);
            // Tonemapping
			if (_FlatCloudsParams.w == 1)	col.rgb = tonemapACES(col.rgb, _CloudsExposure);
            col.a = saturate(density * cloudExtinction);
            
            UNITY_TRANSFER_FOG(OUT,4); 
            return col;
        }
        ENDCG
    }
}


Fallback Off
CustomEditor "SkyboxProceduralShaderGUI"
}
