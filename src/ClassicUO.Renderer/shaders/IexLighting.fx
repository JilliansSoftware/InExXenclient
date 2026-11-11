// IEX: enhanced lighting. Two techniques, both drawn through the sprite batcher
// (same vertex layout as IsometricWorld.fx):
//   Blur      - one direction of a soft blur of the light map (run twice, at half size)
//   Composite - the world picture lit by the light map, in place of the stock multiply blend
// The world sprites are only ever multiplied by light here, never filtered or repainted.
// Compile with compile_shaders.bat (fxc, fx_2_0 container, shader model 3 like the world shader).

float4x4 MatrixTransform;
float2 Viewport;

// Blur: one texel step along the blur direction, in texture coordinates.
float2 BlurStep;

// Composite
float3 Ambient;        // the darkness colour the light map was cleared to
float Soften;          // 0..1, how much of the blurred light map replaces the sharp one
float GlowStrength;    // how far bright cores bleed and lift the picture
float2 LightMapSize;   // light map size in pixels, for the dither pattern
float3 GradeTint;      // colour balance of the finished picture (white = untouched)
float Saturation;      // 1 = untouched, lower drains colour (night, storms)
float Vignette;        // 0 = none, darkening of the screen edges (underground, deep night)

sampler DrawSampler : register(s0);
// s1/s2 hold the hue tables and s3 is reset to point sampling by the batcher: use s4/s5.
sampler LightSampler : register(s4);
sampler SoftSampler : register(s5);

struct VS_INPUT
{
	float4 Position : POSITION0;
	float3 Normal	: NORMAL0;
	float3 TexCoord : TEXCOORD0;
	float3 Hue		: TEXCOORD1;
};

struct PS_INPUT
{
	float4 Position : POSITION0;
	float3 TexCoord : TEXCOORD0;
};

PS_INPUT VertexShaderFunction(VS_INPUT IN)
{
	PS_INPUT OUT;

	OUT.Position = mul(IN.Position, MatrixTransform);

	// same half pixel nudge as IsometricWorld.fx, so the picture lands on the same pixels
	OUT.Position.x -= 0.5 / Viewport.x;
	OUT.Position.y += 0.5 / Viewport.y;

	OUT.TexCoord = IN.TexCoord;

	return OUT;
}

// 9 tap gaussian done as 5 linear samples
float4 PixelShader_Blur(PS_INPUT IN) : COLOR0
{
	float2 uv = IN.TexCoord.xy;

	float3 c = tex2D(DrawSampler, uv).rgb * 0.2270270270f;
	c += tex2D(DrawSampler, uv + BlurStep * 1.3846153846f).rgb * 0.3162162162f;
	c += tex2D(DrawSampler, uv - BlurStep * 1.3846153846f).rgb * 0.3162162162f;
	c += tex2D(DrawSampler, uv + BlurStep * 3.2307692308f).rgb * 0.0702702703f;
	c += tex2D(DrawSampler, uv - BlurStep * 3.2307692308f).rgb * 0.0702702703f;

	return float4(c, 1.0f);
}

float4 PixelShader_Composite(PS_INPUT IN) : COLOR0
{
	float2 uv = IN.TexCoord.xy;

	float4 world = tex2D(DrawSampler, uv);
	float3 sharp = tex2D(LightSampler, uv).rgb;
	float3 soft = tex2D(SoftSampler, uv).rgb;

	// the stock light sprites have 32 brightness steps: mixing in the blurred map hides the rings
	float3 light = lerp(sharp, soft, Soften);

	// glow: only what light sources add over the darkness, and only near their bright cores
	float3 added = max(soft - Ambient, 0.0f);
	float core = smoothstep(0.45f, 1.0f, max(soft.r, max(soft.g, soft.b)));
	light += added * core * GlowStrength;

	// a pixel of noise under one colour step breaks up what banding is left in dark gradients
	float2 p = floor(uv * LightMapSize);
	float noise = frac(52.9829189f * frac(dot(p, float2(0.06711056f, 0.00583715f))));
	light += (noise - 0.5f) / 255.0f;

	float3 lit = world.rgb * max(light, 0.0f);

	// grade: the whole picture, lit areas included, leans with the hour and the weather
	float luma = dot(lit, float3(0.2126f, 0.7152f, 0.0722f));
	lit = lerp(float3(luma, luma, luma), lit, Saturation) * GradeTint;

	// vignette: the corners fall away
	float2 q = uv * 2.0f - 1.0f;
	lit *= 1.0f - Vignette * smoothstep(0.55f, 1.35f, length(q));

	return float4(lit, world.a);
}

technique Blur
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShaderFunction();
		PixelShader = compile ps_3_0 PixelShader_Blur();
	}
}

technique Composite
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShaderFunction();
		PixelShader = compile ps_3_0 PixelShader_Composite();
	}
}
