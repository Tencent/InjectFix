@set UNITY_HOME=D:\Program Files\Unity523f1
if exist "%UNITY_HOME%\Editor\Data\Mono\bin\gmcs" (
    @set GMCS="%UNITY_HOME%\Editor\Data\Mono\bin\gmcs"
) else (
    @set GMCS="%UNITY_HOME%\Editor\Data\MonoBleedingEdge\bin\mcs.bat"
)
@set MONO="%UNITY_HOME%\Editor\Data\MonoBleedingEdge\bin\mono"
@set DLL_OUTPUT=..\UnityProj\Assets\Plugins\IFix.Core.dll
@set TOOL_KIT_PATH=..\UnityProj\IFixToolKit
call %GMCS% ShuffleInstruction.cs -out:.\ShuffleInstruction.exe
%MONO% ShuffleInstruction.exe Src\Core\Instruction.cs Instruction.cs
call %GMCS% -define:UNITY_IPHONE -unsafe -target:library -out:%DLL_OUTPUT% Src\Builder\*.cs Src\Version.cs Instruction.cs ^
Src\Core\AnonymousStorey.cs ^
Src\Core\DataDefine.cs ^
Src\Core\GenericDelegate.cs ^
Src\Core\Il2CppSetOptionAttribute.cs ^
Src\Core\ObjectClone.cs ^
Src\Core\ReflectionMethodInvoker.cs ^
Src\Core\StackOperation.cs ^
Src\Core\SwitchFlags.cs ^
Src\Core\Utils.cs ^
Src\Core\VirtualMachine.cs ^
Src\Core\WrappersManager.cs
md %TOOL_KIT_PATH%
copy /Y ThirdParty\Mono.Cecil* %TOOL_KIT_PATH%
call %GMCS% -define:UNITY_IPHONE -unsafe -reference:ThirdParty\Mono.Cecil.dll,ThirdParty\Mono.Cecil.Mdb.dll,ThirdParty\Mono.Cecil.Pdb.dll -out:%TOOL_KIT_PATH%\IFix.exe -debug Instruction.cs Src\Tools\*.cs Src\Version.cs
pause
