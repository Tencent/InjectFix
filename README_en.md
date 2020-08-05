![Logo](./Pic/logo.png)

[![license](http://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Tencent/InjectFix/blob/master/LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-blue.svg)](https://github.com/Tencent/InjectFix/pulls)
[![Build status](https://travis-ci.org/Tencent/InjectFix.svg?branch=master)](https://travis-ci.org/Tencent/InjectFix)

## Unity code logic hotfix

Can be used for bug fixes in Unity, supporting Unity’s full range

## Highlights

* Modify C# directly on the Unity project to bug fixes
* No need to modify the original code of projects 
* A private patch format for each game for improved security


## Installation

### Compilation

* Open the "Source\VSProj\build_for_unity.bat" file in the source package in Windows, and change the value of the UNITY_HOME parameter to the local installation directory
* Run build_for_unity.bat

### Copy

[Here](./Source/UnityProj/) corresponds to a Unity project directory

* Copy IFixToolKit to a sibling directory of Assets in the Unity project
* Copy Assets/IFix and Assets/Plugins under Assets in the Unity project 

## Doc

* [Quick Start](./Doc/quick_start_en.md)
* [Manual](./Doc/user_manual_en.md)
* [FAQ](./Doc/faq_en.md)
