## 执行Patch for android或者Patch for ios时，报“"please put template file for android/ios in IFixToolKit directory!”

解决这个错误需要制作一个编译模版文件放到IFixToolKit目录：

* 假如你制作的是android的模版，请执行一次普通的android构建，在构建的过程中，到“工程目录/Temp”目录把UnityTempFile打头的文件都拷贝出来，其中一个“UnityTempFile开头”的文件就是你刚刚打包命令行文件，可以根据文件时间或者文件里头的命令行参数（ios会有这么一行：-define:UNITY_IOS，android会有-define:UNITY_ANDROID，而且必须没有UNITY_EDITOR）找到它，拷贝到IFixToolKit目录，如果你在window获取的UnityTempFile，重命名为android.win.tpl，如果是mac下获取的，重命名为android.osx.tpl；（这步对于一个项目，如果你不升级unity版本，不更改条件编译宏，仅需做一次即可）
* ios的模版文件改名为ios.osx.tpl，如果你希望在window下制作ios补丁，复制一份改名为ios.win.tpl，打开这个文件，把链接的引擎dll，系统级dll的路径按window的unity安装目录修改。

## IL2CPP 出现报错`IL2CPP error for method 'System.Object IFix.Core.EvaluationStackOperation::ToObject(IFix.Core.Value*,IFix.Core.Value*,System.Object[],System.Type,IFix.Core.VirtualMachine,System.Boolean)'`

应该是自己手动编译`IFix.Core.dll`导致

修改iFix.Core源代码后，需要通过`build_for_unity.bat`脚本进行构建

## 生成 Patch 的时候遇到`Error: the new assembly must not be inject, please reimport the project!`报错

生成 Patch 的 dll，不能进行注入

## 补丁制作的条件编译宏如何处理

如果是Unity2018.3版本及以上，由于Unity开放了C#编译接口，所以InjectFix在Unity2018.3版本直接支持Android和iOS的补丁生成，直接执行对应菜单即可。

但如果低于Unity2018.3版本，则要用比较麻烦的方式：按对应平台的编译参数把Assembly-CSharp.dll编译出来，然后调用IFix.Editor.IFixEditor.GenPatch去生成补丁。

Unity编译是在工程的Temp目录新建一个文件，把命令行参数放到那个文件，然后执行类似（目录根据自己的unity安装情况而定）如下命令进行编译：

~~~bash
"D:\Program Files\Unity201702\Editor\Data\MonoBleedingEdge\bin\mono.exe" "D:\Program Files\Unity201702\Editor\Data\MonoBleedingEdge\lib\mono\4.5\mcs.exe"  @Temp/UnityTempFile-55a959adddae39f4aaa18507dd165989
~~~

你可以尝试一次编辑器下的手机版本打包，然后到工程目录下的Temp目录把那个临时文件拷贝出来（编译完会自动删掉，所以要手快）。

这个文件大多数地方都不会变的，变的主要是C#文件列表，可以改为动态生成这个文件：C#文件列表根据当前项目生成，其它保持不变。然后用这个文件作为输入来编译。
