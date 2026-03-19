using UnityEngine;

/// <summary>
/// 位姿消费组件：将 TrackingDecoder 输出的 PoseData 应用到当前物体。
///
/// 坐标关系：
/// - poseMatrix 提供目标在参考系下的位置与朝向。
/// - 本组件将其作为 target 的相对位姿进行组合。
/// </summary>
public class CubeFollow : MonoBehaviour
{
    [Header("Reference Transform")]
    [Tooltip("位姿参考系（通常是相机根节点或世界锚点）。")]
    public Transform target;

    /// <summary>
    /// 在 Decoder 事件中调用：根据 4x4 pose 更新当前对象变换。
    /// </summary>
    /// <remarks>
    /// 如果当前帧无 pose（HasPose=false），函数直接返回，不做位姿更新。
    /// </remarks>
    public void FollowTarget(PoseData pose)
    {
        if (!pose.HasPose)
        {
            return;
        }

        var poseMatrix = pose.PoseMatrix.Value;
        var position = new Vector3(poseMatrix.m03, poseMatrix.m13, poseMatrix.m23);
        var rotation = Quaternion.LookRotation(
            new Vector3(poseMatrix.m02, poseMatrix.m12, poseMatrix.m22),
            new Vector3(poseMatrix.m01, poseMatrix.m11, poseMatrix.m21)
        );
        transform.localPosition = target.position + position;
        transform.localRotation = rotation * target.rotation;
    }
}
