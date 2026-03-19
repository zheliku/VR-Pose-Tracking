using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Meta.XR;

public class CameraViewerManager : MonoBehaviour
{
    [SerializeField]
    private PassthroughCameraAccess _leftCameraAccess;
    [SerializeField]
    private PassthroughCameraAccess _rightCameraAccess;

    [SerializeField]
    private RawImage _leftImage;
    [SerializeField]
    private RawImage _rightImage;

    private Texture _leftCameraTexture;
    private Texture _rightCameraTexture;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    IEnumerator Start()
    {
        while (!TryGetCameraAccess())
        {
            yield return null;
        }

        _leftImage.texture  = _leftCameraTexture;
        _rightImage.texture = _rightCameraTexture;
    }

    private bool TryGetCameraAccess()
    {
        if (!_leftCameraAccess || !_leftCameraAccess.IsPlaying || !_rightCameraAccess || !_rightCameraAccess.IsPlaying)
        {
            return false;
        }

        if (_leftCameraAccess && _rightCameraAccess)
        {
            return true;
        }

        _leftCameraTexture  = _leftCameraAccess.GetTexture();
        _rightCameraTexture = _rightCameraAccess.GetTexture();

        if (_leftCameraTexture)
        {
            var resolution = _leftCameraAccess.CurrentResolution;
            print($"[{nameof(CameraViewerManager)}] Left Passthrough texture ready: {resolution.x}x{resolution.y}");
        }
        else
        {
            Debug.LogWarning($"[{nameof(CameraViewerManager)}] Left Passthrough texture not available yet.");
        }

        if (_rightCameraTexture)
        {
            var resolution = _rightCameraAccess.CurrentResolution;
            print($"[{nameof(CameraViewerManager)}] Right Passthrough texture ready: {resolution.x}x{resolution.y}");
        }
        else
        {
            Debug.LogWarning($"[{nameof(CameraViewerManager)}] Right Passthrough texture not available yet.");
        }

        return _leftCameraTexture && _rightCameraTexture;
    }
}