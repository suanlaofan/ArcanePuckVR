Shader "ArcanePuck/Starfield Skybox"
{
    Properties
    {
        _DeepSpaceColor ("Deep Space", Color) = (0.003, 0.001, 0.012, 1)
        _HorizonColor ("Horizon", Color) = (0.035, 0.008, 0.065, 1)
        _NebulaColorA ("Nebula Violet", Color) = (0.30, 0.025, 0.50, 1)
        _NebulaColorB ("Nebula Purple", Color) = (0.08, 0.015, 0.24, 1)
        _StarColor ("Star Color", Color) = (0.92, 0.86, 1, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.90
        _StarIntensity ("Star Intensity", Range(0, 6)) = 4.2
        _NebulaIntensity ("Nebula Intensity", Range(0, 1.5)) = 0.68
        _Exposure ("Exposure", Range(0, 4)) = 1.15
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _DeepSpaceColor;
            float4 _HorizonColor;
            float4 _NebulaColorA;
            float4 _NebulaColorB;
            float4 _StarColor;
            float _StarDensity;
            float _StarIntensity;
            float _NebulaIntensity;
            float _Exposure;
            float _Rotation;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int octave = 0; octave < 5; octave++)
                {
                    value += Noise(p) * amplitude;
                    p = p * 2.03 + float3(1.7, 9.2, 4.1);
                    amplitude *= 0.5;
                }
                return value;
            }

            float StarLayer(float3 direction, float scale, float threshold, float radius, float seed, out float temperature)
            {
                float3 p = direction * scale;
                float3 cell = floor(p);
                float3 local = frac(p) - 0.5;
                float3 random = Hash33(cell + seed);
                float3 offset = (random - 0.5) * 0.72;
                float distanceToStar = length(local - offset);
                float spawn = step(threshold, Hash31(cell + seed * 3.17));
                float halo = 1.0 - smoothstep(0.0, radius, distanceToStar);
                float core = 1.0 - smoothstep(0.0, radius * 0.26, distanceToStar);
                temperature = random.z;
                return spawn * (halo * 0.24 + core * 1.65);
            }

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = mul((float3x3)unity_ObjectToWorld, input.vertex.xyz);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float angle = radians(_Rotation);
                float sineAngle = sin(angle);
                float cosineAngle = cos(angle);
                direction.xz = float2(
                    direction.x * cosineAngle - direction.z * sineAngle,
                    direction.x * sineAngle + direction.z * cosineAngle);

                float vertical = saturate(direction.y * 0.5 + 0.5);
                float3 color = lerp(_HorizonColor.rgb, _DeepSpaceColor.rgb, pow(vertical, 0.62));

                float3 galacticNormal = normalize(float3(0.28, 0.82, -0.49));
                float bandDistance = abs(dot(direction, galacticNormal));
                float band = exp(-bandDistance * bandDistance * 12.0);
                float cloud = Fbm(direction * 3.1 + float3(3.2, 7.1, 1.8));
                float wisps = Fbm(direction * 9.2 + float3(13.4, 2.7, 8.9));
                float purpleHaze = (0.035 + cloud * 0.08 + wisps * 0.025) * (0.35 + band * 0.65);
                color += _NebulaColorA.rgb * purpleHaze * _NebulaIntensity;
                float nebulaMask = band * saturate((cloud - 0.30) * 2.2) * (0.62 + wisps * 0.78);
                float nebulaBlend = saturate(Noise(direction * 5.4 + 18.7));
                color += lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, nebulaBlend) * nebulaMask * _NebulaIntensity;

                float densityOffset = lerp(0.035, 0.18, saturate(_StarDensity));
                float temperatureA;
                float temperatureB;
                float starsA = StarLayer(direction, 155.0, 0.985 - densityOffset, 0.17, 7.3, temperatureA);
                float starsB = StarLayer(direction, 330.0, 0.992 - densityOffset * 0.42, 0.12, 19.1, temperatureB);
                float stars = starsA + starsB * 0.82;
                float temperature = saturate((temperatureA * starsA + temperatureB * starsB) / max(starsA + starsB, 0.001));
                float3 warmStar = float3(1.0, 0.72, 0.48);
                float3 starTint = lerp(warmStar, _StarColor.rgb, temperature);
                color += starTint * stars * _StarIntensity;

                return float4(color * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
