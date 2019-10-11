## Quick Start

### Access example

Check the patch to load the patch

~~~csharp
var patchPath = "./Assets/IFix/Resources/Assembly-CSharp.ill.bytes";
if (File.Exists(patchPath))
{
    PatchManager.Load(new FileStream(patchPath, FileMode.Open));
}
~~~

### Configuration

The implementation of the hotfix relies on the static code insertion in advance, so you need to configure which classes to preprocess so that they can be fixed. In general, classes that are not extremely demanding in performance can all be added.

iFix supports both dynamic and static lists, and dynamic lists are more convenient because there are many different classes. The following is an example where all the classes except the anonymous class in the XLua namespace are configured.

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

__ Key notes: __

* Put a Configure tag on the class to be configured
* Put a IFix tag for the configure attribute, which must be of __static__ class

Dynamic configuration, in addition to not having to configure one by one, may have other additional benefits. Take the above case for example, subsequent class addition/deletion in the namespace will not require modification of the configuration.

Once configured, the mobile package version will automatically conduct pre-process. If you want to automate the packaging, you can also manually call the IFix.Editor.IFixEditor.InjectAllAssemblys function.

### Create a patch

Put a Patch tag on the function that needs to be patched

~~~csharp
[Patch]
public int Add(int a, int b)
{
    return a + b;
}
~~~

Execute the "InjectFix/Fix" menu.

After the patch is successfully created, it will be placed in the project directory with the file name “{Dll Name}.patch.bytes” (for example: “Assembly-CSharp.patch.bytes”). Upload the patch to the phone and load it to see the effect.

Note: If there is a conditional compilation macro for the function to be patched, such as the following code:

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

If you still generate the patch directly in the editor, it will call one more Foo and one less Bar than on the phone, which may lead to various problems, such as wrong logic, or the editor-specific function is called so that the function to be called cannot be found and so on.

When this occurs, you can compile the Assembly-CSharp.dll according to the compile parameters of the platform, and then call IFix.Editor.IFixEditor.GenPatch to generate the patch.

Unity compilation is to create a new file in the project's Temp directory, put the command line parameters into the file, and then compile by executing a command similar (the directory is based on your own unity installation) to the following:

~~~bash
"D:\Program Files\Unity201702\Editor\Data\MonoBleedingEdge\bin\mono.exe" "D:\Program Files\Unity201702\Editor\Data\MonoBleedingEdge\lib\mono\4.5\mcs.exe"  @Temp/UnityTempFile-55a959adddae39f4aaa18507dd165989
~~~

You can try the phone version packaging in the editor, and then copy the temporary file in the Temp directory in the project directory (it will be deleted automatically after compilation, so you need to be fast).

This file will remain unchanged for the most part, with the only change being the C# file list, where it can be changed to be dynamically generated. So the C# file list is generated based on the current project, and the other parts remain unchanged. Then use this file as the input to compile.

##Explore iFix in the editor

In the above process, the patch is loaded on the phone, but if you want to quickly explore the hotfix capability of iFix in the editor, check this document: ["Explore Hotfix in The Editor"](./example_en.md)


