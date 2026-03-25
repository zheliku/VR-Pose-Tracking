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

    [ShowInInspector]
    public PlayerPrefsProperty<float> A;

    [SerializeField]
    private float tt;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Dict["one"] = 1;
        tt = PlayerPrefs.GetFloat("tt", 0f);
        A = new PlayerPrefsProperty<float>("A", 0f);
    }

    // Update is called once per frame
    void Update()
    {

    }

    [OnValueChanged(nameof(tt))]
    private void Test1()
    {
        Debug.Log("tt changed: " + tt);
        PlayerPrefs.SetFloat("tt", tt);
    }
}

[Serializable]
public class PlayerPrefsProperty<T>
{
    public PlayerPrefsProperty(string saveKey, T defaultValue = default)
    {
        this.saveKey = saveKey;
        mValue = defaultValue;
        PlayerPrefs.SetFloat(saveKey, Convert.ToSingle(defaultValue));
    }

    protected T mValue;

    private string saveKey;

    public static Func<T, T, bool> Comparer { get; set; } = (a, b) => a.Equals(b);

    public PlayerPrefsProperty<T> WithComparer(Func<T, T, bool> comparer)
    {
        Comparer = comparer;
        return this;
    }

    [ShowInInspector]
    public T Value
    {
        get => GetValue();
        set
        {
            if (value == null && mValue == null) return;
            if (value != null && Comparer(value, mValue)) return;

            SetValue(value);

            PlayerPrefs.SetFloat(saveKey, Convert.ToSingle(value));
        }
    }

    protected virtual void SetValue(T newValue) => mValue = newValue;

    protected virtual T GetValue() => mValue;

    public void SetValueWithoutEvent(T newValue) => mValue = newValue;

    public override string ToString() => Value.ToString();
}