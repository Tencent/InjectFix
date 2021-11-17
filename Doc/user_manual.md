# IFix使用手册

### [IFix.Patch]

##### 用途         

​		在补丁阶段使用；原生代码修复。如果发现某个函数有错误，就可以使用该标签给函数打补丁，打上这个标签的函数，童鞋们就可以随意修改该函数。  

##### 用法

​		该标签只能用在方法上，直接在要修改的函数上面标注一下这个标签即可。

##### 举例

​		这个函数本来的意思是两个值相加，但现在写错了，所以可以给该函数打上[IFix.Patch]标签，然后修改就可以了

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

##### 用途         

​		在补丁阶段使用；新增代码。在补丁阶段，童鞋们还有新的需求，想新增个字段，函数或者类，可以用[IFix.Interpret]标签实现。  

##### 用法

​		该标签可以用在字段，属性，方法，类型上，直接在要新增的代码上面标注一下这个标签即可。

##### 举例

​		新增一个字段

```c#
public class Test
{
    [IFix.Interpret]
    public int intValue = 0;
}
```

​		新增一个属性

```c#
private string name;//这个name字段是原生的

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

[IFix.Interpret]
public string Id
{
    set;
    get;
}

```

​		新增一个函数

```c#
[IFix.Interpret]
public int Sub(int a,int b)
{
    return a-b;
}
```

​		新增一个类

```c#
[IFix.Interpret]
public class NewClass
{
    ...
}
```

### [IFix.CustomBridge]

##### 用途         

​		在注入阶段使用； 把一个虚拟机的类适配到原生interface或者把一个虚拟机的函数适配到原生delegate。 

​		什么时候需要用到呢？ 

- 修复代码赋值一个闭包到一个delegate变量；
- 修复代码的Unity协程用了yield return；
- 新增一个函数，赋值到一个delegate变量；
- 新增一个类，赋值到一个原生interface变量；
- 新增函数，用了yield return； 

##### 用法

​		该标签只能用在类上，在童鞋们程序的某个地方，写上一个静态类，里面有一个静态字段，值就是interface和delegate的类型集合
        
        ！！注意，该配置类不能放到Editor目录，且不能内嵌到另外一个类里头。

##### 举例

​		新增一个类，该类实现了一个接口

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

​		新增函数（或者修复代码[IFix.Patch]的Unity协程），用到了 yield return 

```c#
[IFix.Interpret]
public IEnumerator TestInterface()
{
    yield return new WaitForSeconds(1);
    UnityEngine.Debug.Log("wait one second");
}
```

​		新增函数（或者修复代码[IFix.Patch]），赋值到一个delegate变量

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

##### 用途         

​		在注入阶段使用；配置类，里面存储的是一些注入时需要注入或过滤的东西。

##### 用法

​		该标签只能用在类上，该类必须在Editor文件夹下  。

##### 举例

```c#
[Configure]
public class TestCfg
{
    
}
```

### [IFix]

##### 用途         

​         在注入阶段使用；用来存储所有你认为将来可能会需要修复的类的集合。该标签和[IFix.Patch]有关联，因为如果发现某个函数需要修复，直接打上[IFix.Patch]标签就可以了，但是前提是，这个需要修复的函数的类必须在[IFix]下。

##### 用法

​		该标签只能用在属性上，Configure类中的一个静态属性，get得到的是可能会需要修复的函数所有类的集合  

##### 举例

​		认为Test类里面的函数可能会出错，所以把它们放到[IFix]标签下，当Test类中的Add函数需要修复，直接打标签修改即可。

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

##### 用途         

​         在注入阶段使用；用来存储想要过滤的东西。在注入阶段，凡是在[IFix]标签下的属性里面的值，都会被注入适配代码，但是如果不想对某个函数进行注入，可以用该标签进行过滤。  

##### 用法

​		该标签只能用在方法上，Configure类中的一个静态方法。    

##### 举例

​		觉得Test类里的函数可能会需要修复，但是Test类里面的Div和Mult不可能有问题，可以把这两个函数过滤掉。

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



### 注意事项

- 如果觉得某个类的函数可能会需要修复，那么一定要把该类放到Editor目录下[Configure]类的[IFix]静态字段里；然后才可以对某个函数进行[IFix.Patch]。
- 涉及到interface和delegate，如果把一个虚拟机的类适配到原生interface或者把一个虚拟机的函数适配到原生delegate  ，一定要放到[IFix.CustomBridge]类的静态字段里。
- 打上[Configure]标签的类，必须放在Editor目录下。  
- [IFix]，[Filter]这些标签必须放在打上[Configure]标签的类里。
- 在[IFix.Patch]时，不支持修复泛型函数，不支持修复构造函数，不支持在原生类中新增字段。
- 在[IFix.Interpret]时，不支持新增类继承原生类，不支持新增类是泛型类。





### 总结

|        标签         | 使用阶段 |            用途            |                             用法                             |
| :-----------------: | :------: | :------------------------: | :----------------------------------------------------------: |
|    [IFix.Patch]     |   补丁   |          修复函数          |                        只能放在函数上                        |
|  [IFix.Interpret]   |   补丁   |    新增字段，属性，函数，类型    |                    放在字段，属性，函数，类型上                    |
| [IFix.CustomBridge] |   注入   |  interface和delegate桥接   | 只能放在单独写一个静态类上，存储虚拟机的类适配到原生interface或者虚拟机的函数适配到原生delegate，该类不能放Editor目录 |
|     [Configure]     |   注入   |           配置类           |          只能放在单独写一个存放在Editor目录下的类上          |
|       [IFix]        |   注入   | 可能需要修复函数的类的集合 |            只能放在[Configure]类的一个静态属性上             |
|      [Filter]       |   注入   |     不想发生注入的函数     |            只能放在[Configure]类的一个静态函数上             |

