## 快速入门

### 接入示例

判断有补丁就加载补丁

~~~csharp
var patchPath = "./Assets/IFix/Resources/Assembly-CSharp.ill.bytes";
if (File.Exists(patchPath))
{
    PatchManager.Load(new FileStream(patchPath, FileMode.Open));
}
~~~

### 配置

热补丁的实现依赖于提前做些静态代码插入，所以需要配置对哪些类预处理，配置了才能被修复。一般而言，只要不是性能要求很苛刻的类都可以加入。

iFix支持动态和静态列表方式，由于类型往往比较多，动态列表会方便些。下面是一个实例，配置XLua名字空间下除匿名类之外的所有类型。

~~~csharp
[Configure]
public class InterpertConfig {
    [IFix]
    static IEnumerable<Type> ToProcess
    {
        get
        {
            return (from type in Assembly.Load("Assembly-CSharp").GetTypes()
                    where type.Namespace == "XLua" && !type.Name.Contains("<")
                    select type);
        }
    }
}
~~~

__划下重点：__

* 配置类打上Configure标签
* 配置的属性打上IFix标签，而且必须是 __static__ 类型

动态配置除了不用一个个配，还可能有其它额外好处，比如上述配置，后续该名字空间下增删类，都不需要更改配置。

配置好后，打包手机版本会自动预处理，如果希望自动化打包，也可以手动调用IFix.Editor.IFixEditor.InjectAllAssemblys函数。

### 补丁制作

对需要打补丁的函数打上Patch标签

~~~csharp
[Patch]
public int Add(int a, int b)
{
    return a + b;
}
~~~

#### 如果要修复的函数不含条件编译宏

执行"InjectFix/Fix"菜单。

补丁制作成功后会放到工程目录下，文件名为“{Dll Name}.patch.bytes”（比如：“Assembly-CSharp.patch.bytes”），上传补丁到手机，加载就能看到效果。

#### 如果要修复的函数存在条件编译宏

比如这样的代码：

~~~csharp
[Patch]
public void Job(int a)
{
#if UNITY_EDITOR
    Foo();
#endif

#if !UNITY_EDITOR
    Bar();
#endif
}
~~~

如果还是直接在编辑器下直接生成补丁，将会比手机上运行多调用了个Foo，少调用了个Bar，这可能会导致各种问题：逻辑不对，调用了编辑器专用函数而导致找不到要调用的函数等等。

这种情况请按[FAQ](faq.md)的[《补丁制作的条件编译宏如何处理》](./faq.md#%E8%A1%A5%E4%B8%81%E5%88%B6%E4%BD%9C%E7%9A%84%E6%9D%A1%E4%BB%B6%E7%BC%96%E8%AF%91%E5%AE%8F%E5%A6%82%E4%BD%95%E5%A4%84%E7%90%86)处理。

## 编辑器下体验iFix

上面的使用流程，补丁是要在手机上加载，如果你想在编辑器下快速体验一下iFix的热补丁能力，可以看下这个文档：[《编辑器下体验热补丁》](./example.md)

