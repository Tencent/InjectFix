Take the matching Helloworld as an example to demonstrate the use of IFix in the editor

### I. Preparation

Helloworld is located under the IFix directory

Calc.cs is the code to be fixed, while Helloworld.cs is the test for Calc.cs.

Run the Helloworld scene and check the print at the console. You can see that Calc.cs is incorrect.

### II. Configuration

Like xLua, you have to configure the code to be preprocessed first , so that the preprocessed code can switch to the patch code at runtime.

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

Description:

1. The configuration must be placed under the Editor directory (the example configuration is located in the Helloworld/Editor directory);
2. The classes to be configured need to be labeled with a Configure tag, and the attribute must be labeled as IFix and declared as **static**;
3. It is enough for the attribute to return IEnumerable\&lt;Type\&gt, because Helloworld need only return a List; as it is a getter, you can use linq + reflection to easily configure a large number of classes; for example, to add all classes under the XXX namespace, the code is as following:

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


### III. Process Description

There are two steps: Inject and Fix.

In practice, Inject is a one-time operation when sending the package. It is to pre-process the code, because only the code that has been pre-processed can load patches normally.

Fix is ​​the process to generate a patch based on the dll compiled from the modified code.

Do not execute Inject between the modified code and Fix, otherwise iFix will consider this to be an online version and refuse to generate a patch. In view of this limitation, we have made some adjustments in the experience flow in the editor: first modify the code to the correct logic and generate a patch. Then revert the code and execute Inject to simulate the problematic online version.

### IV. Fix the code and generate a patch

Open Calc.cs, modify it to the correct logic, and attach a Patch tag to the function that will generate the patch (for comparison, only the Add function is labeled with the Patch tag in the example case)

Execute the "InjectFix/Fix" menu

The process success print indicates that it has been processed successfully. We can find the Assembly-CSharp.patch.bytes file in the project root directory, which is the patch file.

### V. Check the result

Rollback Add to the wrong logic and execute the "InjectFix/Inject" menu (only the injected version can load the patch). Then run it, you can see Add is wrong logic now, then copy the Assembly-CSharp.patch.bytes to \Assets\IFix\Resources and re-execute. Now, you can see that it has been fixed to the new logic.