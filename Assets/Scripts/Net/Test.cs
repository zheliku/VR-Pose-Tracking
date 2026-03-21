using RuntimeInspectorNamespace;
using UnityEngine;

public class Test : MonoBehaviour
{
    public RuntimeInspector runtimeInspector;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        runtimeInspector.Inspect(this);
        runtimeInspector.IsLocked = true; // 锁定后，点击层级面板或其他物体，这里的内容也不会变
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
