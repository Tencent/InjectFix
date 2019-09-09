## When executing Patch for android or Patch for ios, you will get the report ""please put template file for android/ios in IFixToolKit directory!”

To fix this error, you need to create a compilation template file and put it in the IFixToolKit directory:

* If you are creating an android template, perform a normal android build. During the build process, copy the files that begin with UnityTempFile in the "Project Directory/Temp" directory, and one of such files is the command line file you have just packaged, which you can find according to the time of the file or the command line parameters in the file (the UNITY_IOS line for ios files and the -define:UNITY_ANDROID line for android files; this must be without UNITY_EDITOR). Copy it to the IFixToolKit directory. If you got the UnityTempFile under Windows, rename it to android.win.tpl, if it was obtained under mac, rename it to android.osx.tpl; (you only need to complete this step once for a project if you do not upgrade the unity version or change the conditional compilation macro)
* The ios template file is renamed to ios.osx.tpl. If you want to create an ios patch under Windows, copy the file and rename it to ios.win.tpl. Open this file, and modify the path of the linked engine dll and system-level dll to the unity installation directory in Windows.

## IL2CPP reports `IL2CPP error for method 'System.Object IFix.Core.EvaluationStackOperation::ToObject(IFix.Core.Value*,IFix.Core.Value*,System.Object[],System.Type,IFix.Core .VirtualMachine, System.Boolean)'`

It is probably caused by manual compilation of `IFix.Core.dll`

After modifying the iFix.Core source code, it needs to be built with the `build_for_unity.bat` script.

##When generating a patch, you get the report “Error: The new assembly must not be injected, please reimport the project!”

The dll that generates Patch cannot be injected
