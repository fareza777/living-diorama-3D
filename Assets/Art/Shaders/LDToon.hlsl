#ifndef LD_TOON_INCLUDED
#define LD_TOON_INCLUDED

// Shared stylised lighting helpers. Keeping the ramp in one place is what stops the
// ground, the foliage and the creatures drifting into three different looks.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Quantise a 0..1 term into soft bands. Softness scales with the band count so raising
// the count never collapses the ramp back into a smooth gradient.
half LD_ToonRamp (half value, half bands, half softness)
{
    bands = max(2.0h, bands);
    half scaled = saturate(value) * bands;
    half index = floor(scaled);
    half f = scaled - index;
    half soft = softness * bands * 0.5h;
    return saturate((index + smoothstep(0.5h - soft, 0.5h + soft, f)) / bands);
}

// Wrapped diffuse: pushes the terminator around the back of the surface, which reads as
// soft bounce light and keeps unlit sides from going dead flat.
half LD_WrappedDiffuse (half3 normalWS, half3 lightDir, half wrap)
{
    half ndl = dot(normalWS, lightDir);
    return saturate((ndl + wrap) / (1.0h + wrap));
}

// One-call stylised main-light term including banded shadows.
half LD_MainLightToon (Light light, half3 normalWS, half bands, half softness,
                       half wrap, half shadowStrength)
{
    half diffuse = LD_ToonRamp(LD_WrappedDiffuse(normalWS, light.direction, wrap), bands, softness);
    half shadow = lerp(1.0h, LD_ToonRamp(light.shadowAttenuation, bands, softness), shadowStrength);
    return diffuse * shadow;
}

// Rim light restricted to the lit hemisphere, so it reads as light catching an edge
// rather than as a cartoon outline.
half LD_Rim (half3 normalWS, half3 viewWS, half3 lightDir, half power)
{
    half fresnel = pow(1.0h - saturate(dot(normalWS, viewWS)), power);
    half mask = saturate(dot(normalWS, lightDir) * 0.5h + 0.5h);
    return fresnel * mask;
}

// Cheap 2D value noise. Not for anything that needs to look organic up close --
// used for terrain micro-variation and wind phase offsets.
half LD_Hash21 (float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return (half)frac((p3.x + p3.y) * p3.z);
}

half LD_Noise21 (float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    half a = LD_Hash21(i);
    half b = LD_Hash21(i + float2(1, 0));
    half c = LD_Hash21(i + float2(0, 1));
    half d = LD_Hash21(i + float2(1, 1));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Wind displacement shared by every piece of foliage, so a whole meadow bends as one
// gust rather than each blade doing its own thing.
float3 LD_Wind (float3 positionWS, float3 positionOS, half influence, half strength,
                half speed, half3 direction)
{
    if (influence <= 0.001h) return positionWS;

    float phase = positionWS.x * 0.35 + positionWS.z * 0.27;
    float t = _Time.y * speed;

    // Two frequencies: a slow swell plus a faster flutter.
    float swell = sin(t + phase);
    float flutter = sin(t * 2.7 + phase * 3.1) * 0.35;

    // Stiffer at the base, looser at the tip.
    float height = saturate(positionOS.y);
    float bend = (swell + flutter) * strength * influence * height * height;

    positionWS.xz += normalize(direction.xz + 0.0001) * bend;
    positionWS.y -= abs(bend) * 0.25;   // keep the tip roughly length-preserving
    return positionWS;
}

#endif // LD_TOON_INCLUDED
