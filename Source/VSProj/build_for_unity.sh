#!/usr/bin/env sh


thisdir=$(cd `dirname $0`;pwd)
cd "$thisdir"

# set -x

UNITY_ROOT="${UNITY_ROOT-/Applications/Unity}"
UNITY_HOME="$UNITY_ROOT/Unity.app"
if [[ ! -d "$UNITY_HOME" ]]; then
	echo "请指定 UNITY_ROOT 路径"
	exit -1
fi
echo "UNITY_HOME: $UNITY_HOME"


MONO="$UNITY_HOME/Contents/MonoBleedingEdge/bin/mono"
GMCS="$UNITY_HOME/Contents/Mono/bin/gmcs"
DLL_OUTPUT="$thisdir/../Assets/IFix/Plugins/IFix.Core.dll"
TOOL_KIT_PATH="$thisdir/../IFixToolKit"

echo "⚠️ 注意⚠️ : 此脚本在打安装包前可运行一次，热更时不可运行, 运行后妥善保管备份 $TOOL_KIT_PATH/IFix.exe 和 Src/Core/Instruction.cs"

# 打乱指令工具
$GMCS ShuffleInstruction.cs -out:./ShuffleInstruction.exe

# 打乱指令, 要强制重新扰乱指令就手动删除 Src/Core/Instruction.cs， 
# 生成后记得保存备份 Src/Core/Instruction.cs
$MONO ShuffleInstruction.exe Instruction.cs Src/Core/Instruction.cs
# git add Src/Core/Instruction.cs

mkdir -p "$thisdir/../Assets/IFix/Plugins/"
$GMCS -define:UNITY_IPHONE -unsafe -target:library -out:$DLL_OUTPUT \
Src/Version.cs \
Src/Builder/*.cs \
Src/Core/*.cs

if [ ! -d $TOOL_KIT_PATH ]; then
    mkdir $TOOL_KIT_PATH
fi

cp -f ThirdParty/Mono.Cecil* $TOOL_KIT_PATH
$GMCS -define:UNITY_IPHONE -unsafe -reference:ThirdParty/Mono.Cecil.dll,ThirdParty/Mono.Cecil.Mdb.dll,ThirdParty/Mono.Cecil.Pdb.dll \
	-out:$TOOL_KIT_PATH/IFix.exe \
	-debug Src/Core/Instruction.cs \
	Src/Tools/*.cs \
	Src/Version.cs
