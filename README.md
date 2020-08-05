![Logo](./Pic/logo.png)

[![license](http://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Tencent/InjectFix/blob/master/LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-blue.svg)](https://github.com/Tencent/InjectFix/pulls)
[![Build status](https://travis-ci.org/Tencent/InjectFix.svg?branch=master)](https://travis-ci.org/Tencent/InjectFix)

[(English Documents Available)](README_en.md)

## Unity代码逻辑热修复

可用于Unity业务的bug修复，支持Unity全系列，全平台。

## 几个亮点

* 直接在Unity工程上修改C#即可更新
* 老项目无需修改原有代码即可使用
* 每个游戏一份私有补丁格式，安全更有保障


## 安装

### 编译

* Window下打开源码包的Source\VSProj\build_for_unity.bat，UNITY_HOME变量的值修改为指向本机unity安装目录
* 运行build_for_unity.bat

### 复制

[这里](./Source/UnityProj/)对应的是一个Unity工程目录

* IFixToolKit拷贝到Unity项目的Assets同级目录
* Assets/IFix，Assets/Plugins拷贝到Unity项目的Assets下

## 文档

* [快速入门](./Doc/quick_start.md)
* [使用手册](./Doc/user_manual.md)
* [FAQ](./Doc/faq.md)

## 技术支持

请通过[issue](https://github.com/Tencent/InjectFix/issues)来咨询及反馈问题，便于后续跟踪及检索。

