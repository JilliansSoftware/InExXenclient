// IEX: enhanced lighting. Lays the light map over the world picture with a shader
// (shaders/IexLighting.fx) instead of the stock multiply blend: softened light edges and a
// glow around bright cores. The game scene asks for it each frame (Requested); RenderTargets
// draws it. If the shader cannot be loaded on this machine the stock lighting is used.

using System;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.IEX
{
    public sealed class IexLightingPass
    {
        private IexLightingEffect _effect;
        private bool _failed;
        private RenderTarget2D _blurA, _blurB;

        /// <summary>Set by the game scene for the frame being drawn; cleared once drawn.</summary>
        public bool Requested;

        /// <summary>The colour the light map was cleared to.</summary>
        public Vector3 Ambient;

        public float Soften = 0.5f;
        public float GlowStrength = 0.35f;

        public bool Available => !_failed;

        /// <summary>Blurs the light map into a half size copy. Call with no render target set and outside a batch.</summary>
        public bool Prepare(UltimaBatcher2D batcher, RenderTarget2D light)
        {
            if (_failed || light == null || light.IsDisposed)
            {
                return false;
            }

            GraphicsDevice device = batcher.GraphicsDevice;

            try
            {
                _effect ??= new IexLightingEffect(device);
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Warn("IEX: enhanced lighting is off, the shader did not load: " + e.Message);

                return false;
            }

            int w = Math.Max(1, light.Width / 2);
            int h = Math.Max(1, light.Height / 2);
            EnsureSize(device, ref _blurA, w, h);
            EnsureSize(device, ref _blurB, w, h);

            _effect.CurrentTechnique = _effect.Blur;

            // two rounds, the second twice as wide: about 24 screen pixels of spread
            BlurInto(batcher, light, _blurA, new Vector2(1f / w, 0f));
            BlurInto(batcher, _blurA, _blurB, new Vector2(0f, 1f / h));
            BlurInto(batcher, _blurB, _blurA, new Vector2(2f / w, 0f));
            BlurInto(batcher, _blurA, _blurB, new Vector2(0f, 2f / h));

            device.SetRenderTarget(null);

            return true;
        }

        /// <summary>Draws the lit world. Call after Prepare, outside a batch.</summary>
        public void Draw(UltimaBatcher2D batcher, RenderTarget2D world, RenderTarget2D light, Rectangle destination, SamplerState worldSampler)
        {
            GraphicsDevice device = batcher.GraphicsDevice;

            _effect.CurrentTechnique = _effect.Composite;
            SetProjection(device);
            _effect.Ambient.SetValue(Ambient);
            _effect.Soften.SetValue(Soften);
            _effect.GlowStrength.SetValue(GlowStrength);
            _effect.LightMapSize.SetValue(new Vector2(light.Width, light.Height));

            device.Textures[4] = light;
            device.Textures[5] = _blurB;
            device.SamplerStates[4] = SamplerState.LinearClamp;
            device.SamplerStates[5] = SamplerState.LinearClamp;

            batcher.Begin(_effect);
            batcher.SetSampler(worldSampler);
            batcher.SetBlendState(null);
            batcher.Draw(world, destination, Vector3.UnitZ, 0f);
            batcher.End();

            // the light map is drawn into again next frame: it must not stay bound as a texture
            device.Textures[4] = null;
            device.Textures[5] = null;
        }

        private void BlurInto(UltimaBatcher2D batcher, Texture2D source, RenderTarget2D target, Vector2 step)
        {
            GraphicsDevice device = batcher.GraphicsDevice;

            device.SetRenderTarget(target);
            SetProjection(device);
            _effect.BlurStep.SetValue(step);

            batcher.Begin(_effect);
            batcher.SetSampler(SamplerState.LinearClamp);
            batcher.SetBlendState(BlendState.Opaque);
            batcher.Draw(source, new Rectangle(0, 0, target.Width, target.Height), Vector3.UnitZ, 0f);
            batcher.SetBlendState(null);
            batcher.SetSampler(null);
            batcher.End();
        }

        // The batcher only feeds its own shader: ours needs the same screen projection.
        private void SetProjection(GraphicsDevice device)
        {
            Viewport viewport = device.Viewport;

            Matrix.CreateOrthographicOffCenter(
                0f,
                viewport.Width,
                viewport.Height,
                0f,
                short.MinValue,
                short.MaxValue,
                out Matrix matrix
            );

            _effect.MatrixTransform.SetValue(matrix);
            _effect.Viewport.SetValue(new Vector2(viewport.Width, viewport.Height));
        }

        private static void EnsureSize(GraphicsDevice device, ref RenderTarget2D target, int width, int height)
        {
            if (target == null || target.IsDisposed || target.Width != width || target.Height != height)
            {
                target?.Dispose();
                target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            }
        }
    }

    internal sealed class IexLightingEffect : Effect
    {
        public IexLightingEffect(GraphicsDevice device) : base(device, IexShaders.GetLightingShader().ToArray())
        {
            MatrixTransform = Parameters["MatrixTransform"];
            Viewport = Parameters["Viewport"];
            BlurStep = Parameters["BlurStep"];
            Ambient = Parameters["Ambient"];
            Soften = Parameters["Soften"];
            GlowStrength = Parameters["GlowStrength"];
            LightMapSize = Parameters["LightMapSize"];

            Blur = Techniques["Blur"];
            Composite = Techniques["Composite"];
        }

        public EffectParameter MatrixTransform { get; }
        public EffectParameter Viewport { get; }
        public EffectParameter BlurStep { get; }
        public EffectParameter Ambient { get; }
        public EffectParameter Soften { get; }
        public EffectParameter GlowStrength { get; }
        public EffectParameter LightMapSize { get; }
        public EffectTechnique Blur { get; }
        public EffectTechnique Composite { get; }
    }

    internal static partial class IexShaders
    {
        [FileEmbed.FileEmbed("shaders/IexLighting.fxc")]
        public static partial ReadOnlySpan<byte> GetLightingShader();
    }
}
