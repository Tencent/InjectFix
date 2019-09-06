## 执行Patch for android或者Patch for ios时，报“"please put template file for android/ios in IFixToolKit directory!”

解决这个错误需要制作一个编译模版文件放到IFixToolKit目录：

* 假如你制作的是android的模版，请执行一次普通的android构建，在构建的过程中，到“工程目录/Temp”目录把UnityTempFile打头的文件都拷贝出来，其中一个“UnityTempFile开头”的文件就是你刚刚打包命令行文件，可以根据文件时间或者文件里头的命令行参数（ios会有这么一行：-define:UNITY_IOS，android会有-define:UNITY_ANDROID，而且必须没有UNITY_EDITOR）找到它，拷贝到IFixToolKit目录，如果你在window获取的UnityTempFile，重命名为android.win.tpl，如果是mac下获取的，重命名为android.osx.tpl；（这步对于一个项目，如果你不升级unity版本，不更改条件编译宏，仅需做一次即可）
* ios的模版文件改名为ios.osx.tpl，如果你希望在window下制作ios补丁，复制一份改名为ios.win.tpl，打开这个文件，把链接的引擎dll，系统级dll的路径按window的unity安装目录修改。

## IL2CPP 出现报错`IL2CPP error for method 'System.Object IFix.Core.EvaluationStackOperation::ToObject(IFix.Core.Value*,IFix.Core.Value*,System.Object[],System.Type,IFix.Core.VirtualMachine,System.Boolean)'`

应该是自己手动编译`IFix.Core.dll`导致

修改iFix.Core源代码后，需要通过`build_for_unity.bat`脚本进行构建

## 生成 Patch 的时候遇到`Error: the new assembly must not be inject, please reimport the project!`报错

生成 Patch 的 dll，不能进行注入
