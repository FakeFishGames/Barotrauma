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
sampler occlusionSampler = sampler_state {
	Texture = <occlusionMap>;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

Texture2D raycastMap;
sampler raycastSampler = sampler_state {
	Texture = <raycastMap>;
	AddressU = WRAP;
	AddressV = WRAP;
};

Texture2D shadowMap;
sampler shadowSampler = sampler_state {
	Texture = <shadowMap>;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

Texture2D visionCirlce;
sampler visionSampler = sampler_state {
	Texture = <visionCirlce>;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

float2 center;
float bias;
float inDist;
float rayLength;
float aspect;

float4x4 visionTransform;

// Create shadow from polar raycast map 
float4 losShadowMapped(VertexShaderOutput input) : COLOR0
{
	float2 norm = center - input.TexCoords;
	norm.x *= aspect;

	float theta = 0.5f + 0.5f * M_1_PI * atan2(norm.y,norm.x);
	float dist = length(norm);

	// Sample the shadow map using the polar coordinates
	float shadow = tex2D(shadowSampler, float2(theta, dist / rayLength)).r;

	// shadow on occluder from 0 at edge to 1 at in dist
	float dist_occluder = dist - tex2D(raycastSampler, float2(theta, 0.0f)).r * rayLength;
	if (tex2D(occlusionSampler, input.TexCoords).r < bias) { shadow = max(0., dist_occluder) / inDist; }

	shadow = 1.0f - shadow;

	return float4(shadow.rrr, 1.0f);
}

// Create shadow from raycastmap directly 
// Use blurred raycastmap (with shadowmap sampler) on top of occluders
float4 losShadowBlurred(VertexShaderOutput input) : COLOR0
{
	float2 norm = center - input.TexCoords;
	norm.x *= aspect;

	float theta = 0.5f + 0.5f * M_1_PI * atan2(norm.y,norm.x);
	float dist = length(norm);

	// Sample the shadow map using the polar coordinates
	float dist_occluder = tex2D(raycastSampler, float2(theta, 0.0f)).r * rayLength;
	float shadow = float(dist_occluder < dist);

	// shadow on occluder from 0 at edge to 1 at in dist
	if (tex2D(occlusionSampler, input.TexCoords).r < bias) {
		dist_occluder = tex2D(shadowSampler, float2(theta, 0.0f)).r * rayLength;
		shadow = max(0., dist - dist_occluder) / inDist;
	}

	shadow = 1.0f - shadow;

	return float4(shadow.rrr, 1.0f);
}

// Create shadow from raycastmap directly 
float4 losShadow(VertexShaderOutput input) : COLOR0
{
	float2 norm = center - input.TexCoords;
	norm.x *= aspect;

	float theta = 0.5f + 0.5f * M_1_PI * atan2(norm.y,norm.x);
	float dist = length(norm);

	// Sample the shadow map using the polar coordinates
	float dist_occluder = tex2D(raycastSampler, float2(theta, 0.0f)).r * rayLength;
	float shadow = float(dist_occluder < dist);

	// shadow on occluder from 0 at edge to 1 at in dist
	if (tex2D(occlusionSampler, input.TexCoords).r < bias) { shadow = max(0., dist - dist_occluder) / inDist; }

	shadow = 1.0f - shadow;

	return float4(shadow.rrr, 1.0f);
}

float4 losShadowMappedObstruct(VertexShaderOutput input) : COLOR0
{
	float shadow = losShadowMapped(input).r;

	shadow = min(shadow, tex2D(visionSampler, mul(float4(input.TexCoords.xy, 0.0f, 1.0f), visionTransform).xy).r);

	return float4(shadow.rrr, 1.0f);
}

float4 losShadowBlurredObstruct(VertexShaderOutput input) : COLOR0
{
	float shadow = losShadowBlurred(input).r;

	shadow = min(shadow, tex2D(visionSampler, mul(float4(input.TexCoords.xy, 0.0f, 1.0f), visionTransform).xy).r);

	return float4(shadow.rrr, 1.0f);
}

float4 losShadowObstruct(VertexShaderOutput input) : COLOR0
{
	float shadow = losShadow(input).r;

	shadow = min(shadow, tex2D(visionSampler, mul(float4(input.TexCoords.xy, 0.0f, 1.0f), visionTransform).xy).r);

	return float4(shadow.rrr, 1.0f);
}

technique losShadowMapped
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadowMapped();
	}
}

technique losShadowBlurred
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadowBlurred();
	}
}

technique losShadow
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadow();
	}
}

technique losShadowMappedObstruct
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadowMappedObstruct();
	}
}

technique losShadowBlurredObstruct
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadowBlurredObstruct();
	}
}

technique losShadowObstruct
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 losShadowObstruct();
	}
}