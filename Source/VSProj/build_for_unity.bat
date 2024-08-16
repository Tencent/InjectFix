@set UNITY_HOME=E:\work\unitysource2019_x3\build\WindowsEditor
@set UNITY_HOME_LIB=E:/work/unitysource2019_x3/build/WindowsEditor/Data/MonoBleedingEdge/lib

@set GMCS="%UNITY_HOME%\Data\MonoBleedingEdge\bin\mcs.bat"
@set CSC="%UNITY_HOME%\Data\Tools\Roslyn\csc"

@set MONO="%UNITY_HOME%\Data\MonoBleedingEdge\bin\mono"
@set DLL_OUTPUT=..\UnityProj\Assets\Plugins\IFix.Core.dll
@set TOOL_KIT_PATH=..\UnityProj\IFixToolKit
call %GMCS% ShuffleInstruction.cs -out:.\ShuffleInstruction.exe
%MONO% ShuffleInstruction.exe Src\Core\Instruction.cs Instruction.cs

call %CSC% /noconfig ^
/reference:%UNITY_HOME_LIB%/mono/4.7.1-api/mscorlib.dll ^
/reference:%UNITY_HOME_LIB%/mono/4.7.1-api/System.dll ^
/reference:%UNITY_HOME_LIB%/mono/4.7.1-api/System.Core.dll ^
/reference:ThirdParty/Unsafe.As.dll ^
/reference:%UNITY_HOME%/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll ^
/reference:%UNITY_HOME%/Data/Managed/UnityEngine/UnityEngine.PhysicsModule.dll @build_response

md %TOOL_KIT_PATH%
copy /Y ThirdParty\Mono.Cecil* %TOOL_KIT_PATH%
call %GMCS% -define:UNITY_IPHONE -unsafe -reference:ThirdParty\Mono.Cecil.dll,ThirdParty\Mono.Cecil.Mdb.dll,ThirdParty\Mono.Cecil.Pdb.dll -out:%TOOL_KIT_PATH%\IFix.exe -debug Src\Core\Instruction.cs Src\Tools\*.cs Src\Version.cs
pause