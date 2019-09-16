#!/usr/bin/env python3
# -*- coding:utf-8 -*-

import json
import os
import platform
from shutil import copyfile


def parse_global_config():
    with open('config.json', 'r') as f:
        content = f.read()
        global_config = json.loads(content)

        if 'windows' in platform.platform().lower():
            global_config['gmcs'] = os.path.join(
                global_config['UnityHome'], 'Editor/Data/Mono/bin/gmcs')
            global_config['mono'] = os.path.join(
                global_config['UnityHome'], 'Editor/Data/MonoBleedingEdge/bin/mono')
        else:
            global_config['gmcs'] = os.path.join(
                global_config['UnityHome'], 'Contents/Mono/bin/gmcs')
            global_config['mono'] = os.path.join(
                global_config['UnityHome'], 'Contents/MonoBleedingEdge/bin/mono')

        return global_config


def main():
    global_config = parse_global_config()

    # 编译 ShuffleInstruction
    os.system(
        "%s ShuffleInstruction.cs -out:./ShuffleInstruction.exe" % (global_config['gmcs']))

    # 生成混淆后的 Instruction.cs
    os.system(
        "%s ShuffleInstruction.exe Src/Core/Instruction.cs Instruction.cs %s" % (global_config['mono'], global_config['ConfuseKey']))

    # 构建 Dll
    cmd_dll = "%s -define:UNITY_IPHONE -unsafe -target:library -out:%s Src/Builder/*.cs Src/Version.cs Instruction.cs \
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
    Src/Core/WrappersManager.cs" % (global_config['gmcs'], global_config['DllOutput'])
    os.system(cmd_dll)

    # 构建 Toolkit
    if not os.path.exists(global_config['ToolKitOutput']):
        os.mkdir(global_config['ToolKitOutput'])

    cecil_files = os.listdir('ThirdParty')
    for file in cecil_files:
        if 'Mono.Cecil' in file:
            copyfile(os.path.join('ThirdParty', file), os.path.join(
                global_config['ToolKitOutput'], file))

    cmd_tool = "%s -define:UNITY_IPHONE -unsafe -reference:ThirdParty/Mono.Cecil.dll,ThirdParty/Mono.Cecil.Mdb.dll,ThirdParty/Mono.Cecil.Pdb.dll -out:%s/IFix.exe -debug Instruction.cs Src/Tools/*.cs Src/Version.cs" % (global_config['gmcs'], global_config['ToolKitOutput'])

    os.system(cmd_tool)


if __name__ == '__main__':
    main()
