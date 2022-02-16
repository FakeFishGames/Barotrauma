struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoords: TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoords: TEXCOORD0;
};

VertexShaderOutput mainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    output.Position = input.Position;
    output.TexCoords = input.TexCoords;

    return output;
}

const float M_1_PI = 0.318309886183790671537767526745028724;

Texture2D occlusionMap;
sampler occlusionSampler = sampler_state { Texture = <occlusionMap>; };

Texture2D raycastMap;
sampler raycastSampler = sampler_state { Texture = <raycastMap>; };

float2 center;
float bias;
float inDist;

float4 losShadow(VertexShaderOutput input) : COLOR0
{
    float2 norm = center - input.TexCoords;

    float theta = 0.5f + 0.5f * M_1_PI * atan2(norm.y,norm.x);

    float dist = length(norm);
    float occlusion = tex2D(occlusionSampler, input.TexCoords).r;
    float dist_occluder = tex2D(raycastSampler, float2(theta, 0.0f)).r;

    // Pixel is occluded if distance from center greater than ray hit
    float shadow = 0.0f;    
    if (occlusion < bias)
        // On top of the occluder, reduce occlusion close to ray hit
        shadow = max(0., dist - dist_occluder) / inDist;
    else
        shadow = float(dist_occluder < dist);

    shadow = 1.0f - shadow;

    return float4(shadow, shadow, shadow, 1.0f);
}

technique losShadow
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 losShadow();
    }
}