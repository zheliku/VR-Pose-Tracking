using RuntimeInspectorNamespace;
using UnityEngine;
using VInspector;

public class Test : MonoBehaviour
{
    [ShowInInspector]
    public int B
    {
        get => PlayerPrefs.GetInt("B", 0);
        set => PlayerPrefs.SetInt("B", value);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {

    }
}
