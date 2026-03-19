using UnityEngine;

public class CubeFollow : MonoBehaviour
{
    public Transform target;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void FollowTarget(PoseData pose)
    {
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
