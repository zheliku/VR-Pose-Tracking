using UnityEngine;

public class StaticStereoTextureProvider : NetMQPayloadProviderBase
{
    [SerializeField] private Texture leftTexture;
    [SerializeField] private Texture rightTexture;
    [Range(30, 100)]
    [SerializeField] private int jpegQuality = 80;

    private Texture _cachedLeftTexture;
    private Texture _cachedRightTexture;
    private byte[] _cachedLeftJpeg;
    private byte[] _cachedRightJpeg;

    public override bool TryGetPayload(out byte[][] payloadParts)
    {
        payloadParts = null;

        if (leftTexture == null || rightTexture == null)
        {
            return false;
        }

        if (_cachedLeftTexture != leftTexture || _cachedRightTexture != rightTexture ||
            _cachedLeftJpeg == null || _cachedRightJpeg == null)
        {
            _cachedLeftJpeg = EncodeTextureToJpeg(leftTexture);
            _cachedRightJpeg = EncodeTextureToJpeg(rightTexture);
            _cachedLeftTexture = leftTexture;
            _cachedRightTexture = rightTexture;
        }

        if (_cachedLeftJpeg == null || _cachedRightJpeg == null)
        {
            return false;
        }

        payloadParts = new[] { _cachedLeftJpeg, _cachedRightJpeg };
        return true;
    }

    private byte[] EncodeTextureToJpeg(Texture source)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
        {
            return null;
        }

        RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Texture2D readback = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);

        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, target);
        RenderTexture.active = target;

        readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        readback.Apply(false, false);

        RenderTexture.active = previous;
        byte[] jpeg = readback.EncodeToJPG(jpegQuality);

        RenderTexture.ReleaseTemporary(target);
        Destroy(readback);

        return jpeg;
    }
}
