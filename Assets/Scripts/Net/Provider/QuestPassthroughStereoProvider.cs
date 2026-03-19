using Meta.XR;
using UnityEngine;

public class QuestPassthroughStereoProvider : NetMQPayloadProviderBase
{
    [SerializeField] private PassthroughCameraAccess leftCameraAccess;
    [SerializeField] private PassthroughCameraAccess rightCameraAccess;
    [Range(30, 100)]
    [SerializeField] private int jpegQuality = 80;

    private RenderTexture _leftRenderTexture;
    private RenderTexture _rightRenderTexture;
    private Texture2D _leftReadbackTexture;
    private Texture2D _rightReadbackTexture;

    public override bool TryGetPayload(out byte[][] payloadParts)
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

    private void OnDestroy()
    {
        ReleaseBuffer(ref _leftRenderTexture, ref _leftReadbackTexture);
        ReleaseBuffer(ref _rightRenderTexture, ref _rightReadbackTexture);
    }
}
