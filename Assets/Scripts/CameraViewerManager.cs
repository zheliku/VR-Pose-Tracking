using System.Collections;
using Meta.XR;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quest 相机纹理显示管理器。
///
/// 用途：
/// - 等待左右 Passthrough 相机可用。
/// - 将左右纹理绑定到 UI RawImage，便于本地可视化联调。
/// </summary>
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

    /// <summary>
    /// 协程启动：持续等待相机准备完成后再绑定 UI 纹理。
    /// </summary>
    IEnumerator Start()
    {
        while (!TryGetCameraAccess())
        {
            yield return null;
        }

        _leftImage.texture = _leftCameraTexture;
        _rightImage.texture = _rightCameraTexture;
    }

    /// <summary>
    /// 尝试获取左右相机纹理。
    /// 返回 true 表示已经可以用于显示。
    /// </summary>
    private bool TryGetCameraAccess()
    {
        if (!_leftCameraAccess || !_leftCameraAccess.IsPlaying || !_rightCameraAccess || !_rightCameraAccess.IsPlaying)
        {
            return false;
        }

        _leftCameraTexture = _leftCameraAccess.GetTexture();
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

        return _leftCameraTexture != null && _rightCameraTexture != null;
    }
}