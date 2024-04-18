using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
//using IFix.Binding;
using IFix.Core;
using IFix.Test;
using UnityEngine;


public class TestClass
{
    public TestClass()
    {
    }

    public TestClass(string data)
    {
        Data = data;
    }

    private string data;

    private string Data
    {
        get { return data; }
        set { data = value; }
    }

    public static int GetValue(int v)
    {
        return v;
    }
}

public class IFixTestClassWrapper
{
    public unsafe static int Invoke(int v)
    {
        return TestClass.GetValue(v);
    }

    public unsafe void Invoke(VirtualMachine virtualMachine, ref Call call, bool isInstantiate)
    {
        TestClass @class = (TestClass)call.GetObject();
        int v = call.GetInt32(sizeof(Value));
        int ret = TestClass.GetValue(v);
        call.PushObjectAsResult(ret, typeof(int));
    }
}

public class InvokeTest : MonoBehaviour
{
    private TestClass testClass;
    private IFixTestClassWrapper ifix;
    private Type @class;

    private PropertyInfo cachedPropertyInfo;
    private MethodInfo cachedMethodInfo;

    private Func<TestClass, string> getDelegate;

    private Func<TestClass, int, int> getValue;

    // Start is called before the first frame update
    void Start()
    {
        //Test();
        AtomAdd();
    }

    public void AtomAdd()
    {
        int result = 0;
        Stopwatch st = new Stopwatch();

        st.Start();
        for (int i = 0; i < 1000000; i++)
        {
            result++;
        }

        st.Stop();
        UnityEngine.Debug.LogError($"normal:{st.ElapsedMilliseconds}ms, result:{result}");

        st.Restart();
        result = 0;
        Parallel.For(0, 10, (index) =>
        {
            for (int i = 0; i < 100000; i++)
            {
                lock (this)
                {
                    result++;
                }
            }
        });
        st.Stop();
        UnityEngine.Debug.LogError($"lock in:{st.ElapsedMilliseconds}ms, result:{result}");

        st.Restart();
        result = 0;
        Parallel.For(0, 10, (index) =>
        {
            lock (this)
            {
                for (int i = 0; i < 100000; i++)
                {
                    result++;
                }
            }
        });
        st.Stop();
        UnityEngine.Debug.LogError($"lock out:{st.ElapsedMilliseconds}ms, result:{result}");
        
        result = 0;
        st.Restart();
        Parallel.For(0, 10, (index) =>
        {
            for (int i = 0; i < 100000; i++)
            {
                Interlocked.Increment(ref result);
            }
        });
        st.Stop();
        UnityEngine.Debug.LogError($"Interlocked.Increment:{st.ElapsedMilliseconds}ms, result:{result}");

        result = 0;
        st.Restart();
        Parallel.For(0, 10, (index) =>
        {
            int originalValue;
            int newValue;
            for (int i = 0; i < 100000; i++)
            {
                do
                {
                    originalValue = result;
                    newValue = originalValue + 1;
                } while (Interlocked.CompareExchange(ref result, newValue, originalValue) != originalValue);
            }
        });
        st.Stop();
        UnityEngine.Debug.LogError($"Interlocked.CompareExchange:{st.ElapsedMilliseconds}ms, result:{result}");
    }


    [ContextMenu("Test")]
    public void Test()
    {
        //PerfTest.CallOrigin();
        //PerfTest.SafeCallExtern();
        //MethodInfo mi = typeof(List<int>).GetMethod("get_Item");
        //var invoker = new System_Collections_Generic_List_1_int_System_Int32get_Item(mi);
        AssetBundle ab =
            AssetBundle.LoadFromFile("D:/work/InjectFix/Source/UnityProj/buildexe/e6b45fcfc63ff8a19a769f547e3c.ab");
    }

    public string GetViaReflection()
    {
        BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        PropertyInfo property = @class.GetProperty("Data", bindingFlags);
        return (string)property.GetValue(testClass, null);
    }

    public string GetViaCacheReflection()
    {
        return (string)cachedPropertyInfo.GetValue(testClass, null);
    }

    public string GetViaDelegate()
    {
        return getDelegate(testClass);
    }

    // Update is called once per frame
    void Update()
    {
    }
}