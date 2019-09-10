#!/usr/bin/env sh
UNITY_HOME="/Applications/Unity2017/Unity.app"
GMCS="$UNITY_HOME/Contents/Mono/bin/gmcs"
MONO="$UNITY_HOME/Contents/MonoBleedingEdge/bin/mono"
DLL_OUTPUT="../UnityProj/Assets/Plugins/IFix.Core.dll"
TOOL_KIT_PATH="../UnityProj/IFixToolKit"

$GMCS ShuffleInstruction.cs -out:./ShuffleInstruction.exe
$MONO ShuffleInstruction.exe Src/Core/Instruction.cs Instruction.cs
$GMCS -define:UNITY_IPHONE -unsafe -target:library -out:$DLL_OUTPUT Src/Builder/*.cs Src/Version.cs Instruction.cs \
Src/Core/AnonymousStorey.cs \
Src/Core/DataDefine.cs \
Src/Core/GenericDelegate.cs \
Src/Core/Il2CppSetOptionAttribute.cs \
Src/Core/ObjectClone.cs \
Src/Core/ReflectionMethodInvoker.cs \
Src/Core/StackOperation.cs \
Src/Core/SwitchFlags.cs \
Src/Core/Utils.cs \
Src/Core/VirtualMachine.cs \
Src/Core/WrappersManager.cs

if [ ! -d $TOOL_KIT_PATH ]; then
    mkdir $TOOL_KIT_PATH
fi

cp -f ThirdParty/Mono.Cecil* $TOOL_KIT_PATH
$GMCS -define:UNITY_IPHONE -unsafe -reference:ThirdParty/Mono.Cecil.dll,ThirdParty/Mono.Cecil.Mdb.dll,ThirdParty/Mono.Cecil.Pdb.dll -out:$TOOL_KIT_PATH/IFix.exe -debug Instruction.cs Src/Tools/*.cs Src/Version.cs
