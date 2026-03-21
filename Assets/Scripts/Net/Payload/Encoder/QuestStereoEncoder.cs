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
    [SerializeField] private bool packStereoIntoSingleJpeg = true;
    [Range(0.25f, 1f)]
    [SerializeField] private float outputScale = 1f;
    [Range(30, 100)]
    [SerializeField] private int jpegQuality = 80;

    private RenderTexture _leftRenderTexture;
    private RenderTexture _rightRenderTexture;
    private RenderTexture _packedRenderTexture;
    private Texture2D _leftReadbackTexture;
    private Texture2D _rightReadbackTexture;
    private Texture2D _packedReadbackTexture;
    private bool _hasLoggedTextureTypes;

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

        LogTextureTypesOnce(leftTexture, rightTexture);

        EnsureCaptureBuffers(leftTexture, rightTexture);

        if (packStereoIntoSingleJpeg)
        {
            BlitToRenderTarget(leftTexture, _leftRenderTexture);
            BlitToRenderTarget(rightTexture, _rightRenderTexture);

            byte[] packedJpeg = CapturePackedStereoAsJpeg(
                _leftRenderTexture,
                _rightRenderTexture,
                _packedRenderTexture,
                _packedReadbackTexture);

            if (packedJpeg == null)
            {
                return false;
            }

            payloadParts = new[] { packedJpeg };
            return true;
        }

        bool leftDirect = TryEncodeTexture2DDirect(leftTexture, out byte[] leftJpeg);
        bool rightDirect = TryEncodeTexture2DDirect(rightTexture, out byte[] rightJpeg);

        if (!leftDirect)
        {
            leftJpeg = CaptureAsJpeg(leftTexture, _leftRenderTexture, _leftReadbackTexture);
        }

        if (!rightDirect)
        {
            rightJpeg = CaptureAsJpeg(rightTexture, _rightRenderTexture, _rightReadbackTexture);
        }

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
        int leftWidth = Mathf.Max(1, Mathf.RoundToInt(leftTexture.width * outputScale));
        int leftHeight = Mathf.Max(1, Mathf.RoundToInt(leftTexture.height * outputScale));
        int rightWidth = Mathf.Max(1, Mathf.RoundToInt(rightTexture.width * outputScale));
        int rightHeight = Mathf.Max(1, Mathf.RoundToInt(rightTexture.height * outputScale));
        int packedWidth = leftWidth + rightWidth;
        int packedHeight = Mathf.Min(leftHeight, rightHeight);

        EnsureBuffer(ref _leftRenderTexture, ref _leftReadbackTexture, leftWidth, leftHeight);
        EnsureBuffer(ref _rightRenderTexture, ref _rightReadbackTexture, rightWidth, rightHeight);
        EnsureBuffer(ref _packedRenderTexture, ref _packedReadbackTexture, packedWidth, packedHeight);
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

        RenderTexture.active = previous;
        return readbackTexture.EncodeToJPG(jpegQuality);
    }

    private static void BlitToRenderTarget(Texture source, RenderTexture target)
    {
        if (source == null || target == null)
        {
            return;
        }

        Graphics.Blit(source, target);
    }

    private bool TryEncodeTexture2DDirect(Texture source, out byte[] jpeg)
    {
        jpeg = null;

        if (!Mathf.Approximately(outputScale, 1f))
        {
            return false;
        }

        if (source is not Texture2D texture2D)
        {
            return false;
        }

        try
        {
            jpeg = texture2D.EncodeToJPG(jpegQuality);
            return jpeg != null && jpeg.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void LogTextureTypesOnce(Texture leftTexture, Texture rightTexture)
    {
        if (_hasLoggedTextureTypes)
        {
            return;
        }

        _hasLoggedTextureTypes = true;
        string leftType = leftTexture.GetType().Name;
        string rightType = rightTexture.GetType().Name;
        Debug.Log($"[QuestStereoEncoder] LeftType={leftType}, RightType={rightType}, OutputScale={outputScale:F2}, PackSingleJpeg={packStereoIntoSingleJpeg}");
    }

    private byte[] CapturePackedStereoAsJpeg(
        RenderTexture leftSource,
        RenderTexture rightSource,
        RenderTexture packedTarget,
        Texture2D packedReadbackTexture)
    {
        if (leftSource == null || rightSource == null || packedTarget == null || packedReadbackTexture == null)
        {
            return null;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = packedTarget;

        GL.PushMatrix();
        GL.LoadPixelMatrix(0, packedTarget.width, packedTarget.height, 0);

        int leftWidth = leftSource.width;
        int rightWidth = rightSource.width;
        int packedHeight = packedTarget.height;

        Graphics.DrawTexture(new Rect(0, 0, leftWidth, packedHeight), leftSource);
        Graphics.DrawTexture(new Rect(leftWidth, 0, rightWidth, packedHeight), rightSource);

        GL.PopMatrix();

        packedReadbackTexture.ReadPixels(new Rect(0, 0, packedTarget.width, packedTarget.height), 0, 0, false);
        RenderTexture.active = previous;
        return packedReadbackTexture.EncodeToJPG(jpegQuality);
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
        ReleaseBuffer(ref _packedRenderTexture, ref _packedReadbackTexture);
    }
}
