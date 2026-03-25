using System;
using System.Collections.Generic;
using RuntimeInspectorNamespace;
using UnityEngine;
using VInspector;

public class Test : MonoBehaviour
{
    [Serializable]
    public class MyClass<T>
    {
        public int a;
        public string b;
        public T c;
    }


    [ShowInInspector]
    public MyClass<string> MyData = new MyClass<string>() { a = 1, b = "hello", c = "world" };

    // [ShowInInspector]
    // [SerializeField]
    public SerializedDictionary<string, int> Dict = new SerializedDictionary<string, int>();

    [ShowInInspector]
    public int B
    {
        get => PlayerPrefs.GetInt("B", 0);
        set => PlayerPrefs.SetInt("B", value);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Dict["one"] = 1;
    }

    // Update is called once per frame
    void Update()
    {

    }
}
