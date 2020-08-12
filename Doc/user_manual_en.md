# IFix Manual

### [IFix.Patch]

##### use

​		Used in the patch stage,native code fix. If you find an error in a function, you can use the label to patch the function, and the function with this label can modify the function at will.

##### usage

​		This label can only be used on functions, just mark this label directly on the function to be modified.

##### eg

​		This function originally meant to add two values, but now it is wrong, so you can label the function with [IFix.Patch] and modify it.

```c#
public int Add(int a,int b)
{
    return a*b;
}
```

```c#
[IFix.Patch]
public int Add(int a,int b)
{
    return a+b;
}
```

### [IFix.Interpret]

##### use

​		Used in the patch stage, add code. In the patch stage, people still have new requirements. If you want to add a function or class, you can use the [IFix.Interpret] label to implement it.

##### usage

​		This label can be used in  properties , functions, and classes. Just mark this label directly on the code to be added.

##### eg

​		Add a new property

```c#
private string name;//The name field is native

public string Name
{
    [IFix.Interpret]
    set
    {
    	name = value;    
    }
    [IFix.Interpret]
    get
    {
        return name;
    }
}
```

​		Add a new function 

```c#
[IFix.Interpret]
public int Sub(int a,int b)
{
    return a-b;
}
```

​		Add a new class

```c#
[IFix.Interpret]
public class NewClass
{
    ...
}
```

### [IFix.CustomBridge]

##### use

​		Used in the injection stage, adapt a virtual machine class to the native interface or adapt a virtual machine function to the native delegate.

​		 When do I need to use it?

- Fix the code to assign a closure to a delegate variable;
- The Unity coroutine that fixes the code uses yield return;
- Add a function and assign it to a delegate variable;
- Add a new class and assign it to a native interface variable;
- Added function, using yield return;

##### usage

​		This label can only be used on the class. Somewhere in people's program, write a static class with a static field and the value is the type collection of interface and delegate.

##### eg

​		Add a new class, which implements an interface.

```c#
public interface ISubSystem
{
	bool running { get; }
    void Print();
}

[IFix.Interpret]
public class SubSystem : ISubSystem
{
    public bool running { get { return true; } }
    public void Print()
    {
        UnityEngine.Debug.Log("SubSystem1.Print");
    }
}
```

​		Add a new function (or Unity coroutine with fixed code [IFix.Patch]), using yield return.

```c#
[IFix.Interpret]
public IEnumerator TestInterface()
{
    yield return new WaitForSeconds(1);
    UnityEngine.Debug.Log("wait one second");
}
```

​		Add a new function (or fix the code [IFix.Patch]) and assign it to a delegate variable.

```c#
public class Test 
{
    public delegate int MyDelegate(int a, int b);
    
    [IFix.Interpret]
    public MyDelegate TestDelegate()
    {
        return (a,b) => a + b;
    }
}
```

```c#
[IFix.CustomBridge]
public static class AdditionalBridge
{
    static List<Type> bridge = new List<Type>()
    {
        typeof(ISubSystem),
        typeof(IEnumerator),
        typeof(Test.MyDelegate)
    };
}
```

### [Configure]

##### use

​		Used in the injection stage, configuration class, which stores some things that need to be injected or filtered during injection.

##### usage

​		The label can only be used on the class, the class must be in the Editor folder.

##### eg

```c#
[Configure]
public class TestCfg
{
    
}
```

### [IFix]

##### use

​		Used in the injection stage, used to store a collection of all classes that you think may need to be fixed in the future. This label is related to [IFix.Patch], because if you find that a function needs to be fixed, just label the [IFix.Patch] label , but the premise is that the class of the function that needs to be fixed must be under [IFix].

##### usage

​		This label can only be used on properties, a static property in the Configure class, get is a collection of all classes of functions that may need to be fixed.

##### eg

​		I think the functions in the Test class may be wrong, so put them under the [IFix] label. When the Add function in the Test class needs to be fixed, just label it and modify it.

```c#
[Configure]
public class TestCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return new List<Type>()
            {
              	typeof(Test)
            };
        }
    }
}

public class Test
{
    [IFix.Patch]
    public int Add(int a,int b)
    {
        return a+b;
    }
}
```

### [Filter]

##### use

​		Used in the injection stage, used to store what you want to filter. In the injection stage, all values in the properties under the [IFix] label will be injected into the adaptation code, but if you don't want to inject a function, you can use this label to filter.

##### usage

​		This label can only be used on functions, a static function in the Configure class.

##### eg

​		I think the functions in the Test class may be wrong, so put them under the [IFix] label. When the Add function in the Test class needs to be fixed, just label it and modify it.

```c#
public class Test
{
    [IFix.Patch]
    public int Add(int a,int b)
    {
        return a+b;
    }
    public int Sub(int a,int b)
    {
        return a-b;
    }    
    public int Div(int a,int b)
    {
        return a/b;
    }
    public int Mult(int a,int b)
    {
        return a*b;
    }
}

[Configure]
public class TestCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return new List<Type>()
            {
              	typeof(Test)
            };
        }
    }
    [Filter]
    static bool Filter(System.Reflection.MethodInfo methodInfo)
    {
        return methodInfo.DeclaringType.FullName == "Test" 
            && (methodInfo.Name == "Div" || methodInfo.Name == "Mult");
    }
}
```

### Precautions

- If you think that a function of a certain class may need to be fixed, you must put the class in the [IFix] static field of the [Configure] class in the Editor directory, then you can perform [IFix.Patch] on a certain function.
- When it comes to interface and delegate, if you adapt a virtual machine class to a native interface or adapt a virtual machine function to a native delegate, you must put it in the static field of the [IFix.CustomBridge] class.
- The class marked with [Configure] must be placed in the Editor directory.
- [IFix], [Filter] These labels must be placed in the class marked with [Configure].
- In [IFix.Patch], it does not support fixing generic functions, repairing constructors, or adding fields to native classes.
- In [IFix.Interpret], new classes are not supported to inherit native classes, and new classes are not supported as generic classes.

### In conclusion

|        Label        | Use stage |                             Use                             |                            Usage                             |
| :-----------------: | :-------: | :---------------------------------------------------------: | :----------------------------------------------------------: |
|    [IFix.Patch]     |   patch   |                        Fix function                         |               Can only be placed on functions                |
|  [IFix.Interpret]   |   patch   |              New properties, functions, types               |               On properties, functions, types                |
| [IFix.CustomBridge] |  inject   |                interface and delegate bridge                | It can only be placed on a separate static class, the class of the storage virtual machine is adapted to the native interface or the function of the virtual machine is adapted to the native delegate |
|     [Configure]     |  inject   |                     Configuration class                     | Can only be placed on a class that is written separately and stored in the Editor directory |
|       [IFix]        |  inject   | The collection of classes that may need to fix the function | Can only be placed on a static property of the [Configure] class |
|      [Filter]       |  inject   |          Functions that do not want to be injected          | Can only be placed on a static function of the [Configure] class |

