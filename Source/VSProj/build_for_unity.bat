@set UNITY_HOME=D:\Program Files\Unity523f1
@set GMCS="%UNITY_HOME%\Editor\Data\Mono\bin\gmcs"
@set MONO="%UNITY_HOME%\Editor\Data\MonoBleedingEdge\bin\mono"
@set DLL_OUTPUT=..\UnityProj\Assets\Plugins\IFix.Core.dll
@set TOOL_KIT_PATH=..\UnityProj\IFixToolKit
call %GMCS% ShuffleInstruction.cs -out:.\ShuffleInstruction.exe
%MONO% ShuffleInstruction.exe Instruction.cs Src\Core\Instruction.cs
call %GMCS% -define:UNITY_IPHONE -unsafe -target:library -out:%DLL_OUTPUT% ^
Src\Version.cs ^
Src\Builder\*.cs ^
Src\Core\*.cs

md %TOOL_KIT_PATH%
copy /Y ThirdParty\Mono.Cecil* %TOOL_KIT_PATH%
call %GMCS% -define:UNITY_IPHONE -unsafe -reference:ThirdParty\Mono.Cecil.dll,ThirdParty\Mono.Cecil.Mdb.dll,ThirdParty\Mono.Cecil.Pdb.dll -out:%TOOL_KIT_PATH%\IFix.exe -debug Src\Core\Instruction.cs Src\Tools\*.cs Src\Version.cs
pause
