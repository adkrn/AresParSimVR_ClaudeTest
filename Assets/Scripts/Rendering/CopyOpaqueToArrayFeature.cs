using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CopyOpaqueToArrayFeature : ScriptableRendererFeature
{
    class CopyPass : ScriptableRenderPass
    {
        static readonly int OPAQUE_TEX_ID = Shader.PropertyToID("_XRSceneColorTex");
        RTHandle _cameraColor;
        ProfilingSampler _prof = new("CopyOpaqueToArray");

        public void Setup(RTHandle src) => _cameraColor = src;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor desc)
        {
            // 2-slice Texture2DArray, same size/format
            desc.dimension        = TextureDimension.Tex2DArray;
            desc.volumeDepth      = 2;                // eyes 0,1
            desc.msaaSamples      = 1;
            desc.useMipMap        = false;
            cmd.GetTemporaryRT(OPAQUE_TEX_ID, desc, FilterMode.Bilinear);
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (!data.cameraData.xr.enabled) return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _prof))
            {
                // Blit → _XRSceneColorTex   (Blitter는 XR-safe)
                Blitter.BlitCameraTexture(cmd, _cameraColor, new RenderTargetIdentifier(OPAQUE_TEX_ID));
            }
            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(OPAQUE_TEX_ID);
        }
    }

    CopyPass _pass;

    public override void Create()
    {
        _pass = new CopyPass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        _pass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(_pass);
    }
}