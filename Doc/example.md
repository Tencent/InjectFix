以配套的Helloworld为例，编辑器下演示IFix的使用

### 一、准备工作

Helloworld位于IFix目录下

其中Calc.cs就是待修复的代码，Helloworld.cs是Calc.cs的测试。

运行一下Helloworld的场景，看下控制台的打印，可以看到Calc.cs是错误的。

### 二、配置

和xLua类似，你得配置下要预处理的代码，预处理过的代码才可能在运行时切换到补丁代码。

~~~csharp
[Configure]
public class HelloworldCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return new List<Type>()
            {
                typeof(IFix.Test.Calculator)
            };
        }
    }
}
~~~

说明下：

1、这配置必须放在Editor目录下（例子的配置位于Helloworld/Editor目录下）；
2、写配置的类需要打上Configure标签，属性必须打IFix标签并且声明为 **static** ；
3、属性返回一个IEnumerable\&lt;Type\&gt;即可，由于Helloworld只需要简单的返回个List，因为这是个getter，你可以用linq+反射很方便的把大量的类给配上，例如你要一次加入XXX命名空间下所有类，可以这样：

~~~csharp
[Configure]
public class HelloworldCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return (from type in Assembly.Load("Assembly-CSharp").GetTypes()
                    where type.Namespace == "XXXX"
                    select type).ToList();
        }
    }
}
~~~


### 三、流程说明

有两个步骤：Inject，Fix。

实际应用中，Inject只需在发包时做一次，这个步骤主要是对代码做一定的预处理，只有做了预处理的代码后续才能正常加载补丁。

Fix的过程是根据修改后的代码编译后的dll，生成补丁。

修改代码和Fix之间别执行Inject，否则iFix会认为这是个线上版本，拒绝生成补丁。鉴于这个限制，我们编辑器下体验流程上做一定的调整：先修改代码为正确逻辑，生成patch。然后回退代码，执行Inject模拟线上有问题的版本。

### 四、修复代码、生成patch

打开Calc.cs，修改为正确的逻辑，为将要生成patch的函数打上Patch标签（为了做对比，案例只为Add函数打Patch标签）

执行"InjectFix/Fix"菜单

看到process success打印表示已经处理成功。我们可以在项目根目录下找到Assembly-CSharp.patch.bytes文件，这就是补丁文件。

### 五、看看效果

回滚对Add为错误逻辑，执行"InjectFix/Inject"菜单（只有注入过的版本才能加载补丁）。然后运行，可以看到Add此时为错误逻辑，然后把Assembly-CSharp.patch.bytes拷贝到\Assets\IFix\Resources下，重新执行，可以看到已经修复到新逻辑。