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

const float M_TAU = 6.28318530717958647692528676655900577;

Texture2D raycastMap;
sampler raycastSampler = sampler_state {
	Texture = <raycastMap>;
	AddressU = WRAP;
	AddressV = WRAP;
};

Texture2D penumbraLut;
sampler penumbraSampler = sampler_state {
	Texture = <penumbraLut>;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

float penumbraAngle;
float rayLength;
float margin;

float4 losPenumbra8(VertexShaderOutput input) : COLOR0
{
	float shadow = 0.0f;

// If behind ray on main occluder is 
float dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x, 0.0f)).r;
if (dist_occluder > margin) shadow = 1.0f; // margin to prevent rounding errors near edge

// penumbra
float angle_increment = penumbraAngle / 10. / M_TAU; // step distance on raycast LUT
float angle_increment_uv = 1. / 10.; // step distance on penumbra LUT
for (int i = 1; i <= 8; i++)
{
	// Get distance of adjacent ray 
	float dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x + float(i) * angle_increment, 0.0f)).r;
	// Use distance to sample occlusion from Penumbra LUT
	float occlusion = tex2D(penumbraSampler, float2(dist_occluder * rayLength, float(i) * angle_increment_uv)).r;
	shadow = max(occlusion, shadow);

	// Second sample in other direction
	dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x - float(i) * angle_increment, 0.0f)).r;
	occlusion = tex2D(penumbraSampler, float2(dist_occluder * rayLength, float(i) * angle_increment_uv)).r;
	shadow = max(occlusion, shadow);
}

return float4(shadow.rrr, 1.0f);
}

float4 losPenumbra4(VertexShaderOutput input) : COLOR0
{
	float shadow = 0.0f;

// If behind ray on main occluder is 
float dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x, 0.0f)).r;
if (dist_occluder > margin) shadow = 1.0f; // margin to prevent rounding errors near edge

// penumbra
float angle_increment = penumbraAngle / 6. / M_TAU; // step distance on raycast LUT
float angle_increment_uv = 1. / 6.; // step distance on penumbra LUT
for (int i = 1; i <= 4; i++)
{
	// Get distance of adjacent ray 
	float dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x + float(i) * angle_increment, 0.0f)).r;
	// Use distance to sample occlusion from Penumbra LUT
	float occlusion = tex2D(penumbraSampler, float2(dist_occluder * rayLength, float(i) * angle_increment_uv)).r;
	shadow = max(occlusion, shadow);

	// Second sample in other direction
	dist_occluder = input.TexCoords.y - tex2D(raycastSampler, float2(input.TexCoords.x - float(i) * angle_increment, 0.0f)).r;
	occlusion = tex2D(penumbraSampler, float2(dist_occluder * rayLength, float(i) * angle_increment_uv)).r;
	shadow = max(occlusion, shadow);
}

return float4(shadow.rrr, 1.0f);
}

technique losPenumbra8
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_3 losPenumbra8();
	}
}

technique losPenumbra4
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_3 losPenumbra4();
	}
}