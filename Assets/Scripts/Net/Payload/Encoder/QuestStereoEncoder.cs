using Meta.XR;
using UnityEngine;

/// <summary>
/// Quest 双目图像编码器。
///
/// 输入：左右 Passthrough Camera 纹理。
/// 输出：multipart [left_jpg, right_jpg]。
///
/// 注意：
/// - 本组件只负责编码，不负责发送。
/// - 发送节奏由 PayloadSender 控制。
/// </summary>
public class QuestStereoEncoder : EncoderBase
{
    [SerializeField] private PassthroughCameraAccess leftCameraAccess;
    [SerializeField] private PassthroughCameraAccess rightCameraAccess;
    [Range(30, 100)]
    [SerializeField] private int jpegQuality = 80;

    private RenderTexture _leftRenderTexture;
    private RenderTexture _rightRenderTexture;
    private Texture2D _leftReadbackTexture;
    private Texture2D _rightReadbackTexture;

    /// <summary>
    /// 从 Quest 左右相机抓取当前帧并编码为 JPEG 双帧 payload。
    /// </summary>
    public override bool TryEncodePayload(out byte[][] payloadParts)
    {
        payloadParts = null;

        if (leftCameraAccess == null || rightCameraAccess == null)
        {
            return false;
        }

        if (!leftCameraAccess.IsPlaying || !rightCameraAccess.IsPlaying)
        {
            return false;
        }

        Texture leftTexture = leftCameraAccess.GetTexture();
        Texture rightTexture = rightCameraAccess.GetTexture();

        if (leftTexture == null || rightTexture == null)
        {
            return false;
        }

        EnsureCaptureBuffers(leftTexture, rightTexture);

        byte[] leftJpeg = CaptureAsJpeg(leftTexture, _leftRenderTexture, _leftReadbackTexture);
        byte[] rightJpeg = CaptureAsJpeg(rightTexture, _rightRenderTexture, _rightReadbackTexture);

        if (leftJpeg == null || rightJpeg == null)
        {
            return false;
        }

        payloadParts = new[] { leftJpeg, rightJpeg };
        return true;
    }

    /// <summary>
    /// 确保左右采集缓冲区尺寸与源纹理一致。
    /// </summary>
    private void EnsureCaptureBuffers(Texture leftTexture, Texture rightTexture)
    {
        EnsureBuffer(ref _leftRenderTexture, ref _leftReadbackTexture, leftTexture.width, leftTexture.height);
        EnsureBuffer(ref _rightRenderTexture, ref _rightReadbackTexture, rightTexture.width, rightTexture.height);
    }

    private void EnsureBuffer(
        ref RenderTexture renderTexture,
        ref Texture2D readbackTexture,
        int width,
        int height)
    {
        bool needsCreate =
            renderTexture == null ||
            readbackTexture == null ||
            renderTexture.width != width ||
            renderTexture.height != height;

        if (!needsCreate)
        {
            return;
        }

        ReleaseBuffer(ref renderTexture, ref readbackTexture);

        renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        readbackTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    /// <summary>
    /// 将 source 纹理复制到 RenderTexture，再回读并编码为 JPEG。
    /// </summary>
    private byte[] CaptureAsJpeg(Texture source, RenderTexture target, Texture2D readbackTexture)
    {
        if (source == null || target == null || readbackTexture == null)
        {
            return null;
        }

        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, target);
        RenderTexture.active = target;

        readbackTexture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        readbackTexture.Apply(false, false);

        RenderTexture.active = previous;
        return readbackTexture.EncodeToJPG(jpegQuality);
    }

    /// <summary>
    /// 释放一侧采集缓冲资源。
    /// </summary>
    private void ReleaseBuffer(ref RenderTexture renderTexture, ref Texture2D readbackTexture)
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        if (readbackTexture != null)
        {
            Destroy(readbackTexture);
            readbackTexture = null;
        }
    }

    /// <summary>
    /// 对象销毁时释放左右缓冲资源，防止纹理泄漏。
    /// </summary>
    private void OnDestroy()
    {
        ReleaseBuffer(ref _leftRenderTexture, ref _leftReadbackTexture);
        ReleaseBuffer(ref _rightRenderTexture, ref _rightReadbackTexture);
    }
}
