
solution "IFix"
    configurations {
        "Debug", "Release"
    }

    location ("./" .. (_ACTION or ""))
    debugdir (".")
    debugargs {  }

    platforms { "Any CPU" }

configuration "Debug"
    symbols "On"
    defines { "_DEBUG", "DEBUG", "TRACE" }
configuration "Release"
    flags { "Optimize" }
configuration "vs*"
    defines { "" }

project "IFix.Core"
language "C#"
kind "SharedLib"
framework "3.5"
targetdir "./Lib"

files
{
    "Src/Core/*.cs",
    "Src/Builder/SimpleVirtualMachineBuilder.cs",
    "Src/Builder/FileVirtualMachineBuilder.cs",
    "Src/Version.cs",
}

defines
{
	"UNITY_IPHONE",
}

links
{
    "System",
    "System.Core",
}

configuration { "Debug*" }
    defines { "DEBUG" }
    symbols "On"
    clr "Unsafe"
 
configuration { "Release*" }
    flags   { "Optimize" }
    clr "Unsafe"
    
project "IFix"
language "C#"
kind "ConsoleApp"
framework "3.5"
targetdir "./Bin"

files
{
    "Src/Core/Instruction.cs",
    "Src/Tools/*.cs",
    "Src/Version.cs",
}

defines
{
	
}

links
{
    "System",
    "System.Core",
    "ThirdParty/Mono.Cecil.dll",
    "ThirdParty/Mono.Cecil.Mdb.dll",
    "ThirdParty/Mono.Cecil.Pdb.dll",
}

configuration { "Debug*" }
    defines { "DEBUG" }
    symbols "On"
    clr "Unsafe"
 
configuration { "Release*" }
    flags   { "Optimize" }
    clr "Unsafe"

project "IFix.TestDLL"
language "C#"
kind "SharedLib"
framework "3.5"
targetdir "./Lib"

files
{
    "Src/TestDLL/*.cs",
}

defines
{
}

links
{
    "System",
    "System.Core",
}

project "IFix.UnitTest"
language "C#"
kind "SharedLib"
framework "3.5"
targetdir "./Lib"

dependson { "IFix.Core", "IFix.TestDLL", "IFix" }

files
{
    "Src/UnitTest/*.cs",
}

defines
{
	
}

links
{
    "System",
    "System.Core",
    "IFix.Core",
    "IFix.TestDLL",
    "Data/IFix.TestDLL.Redirect.dll",
}

configuration { "Debug*" }
    defines { "DEBUG" }
    symbols "On"
    clr "Unsafe"
    prebuildcommands
    { 
        "mkdir ..\\Data",
        "..\\Bin\\IFix.exe -inject ..\\Lib\\IFix.Core.dll ..\\Lib\\IFix.TestDLL.dll no_cfg ..\\Data\\IFix.TestDLL.Redirect.dif ..\\Data\\IFix.TestDLL.Redirect.dll",
    }
 
configuration { "Release*" }
    flags   { "Optimize" }
    clr "Unsafe"
    prebuildcommands
    { 
        "mkdir ..\\Data",
        "..\\Bin\\IFix.exe -inject ..\\Lib\\IFix.Core.dll ..\\Lib\\IFix.TestDLL.dll no_cfg ..\\Data\\IFix.TestDLL.Redirect.dif ..\\Data\\IFix.TestDLL.Redirect.dll",
    }

project "IFix.PerfTest"
language "C#"
kind "ConsoleApp"
framework "3.5"
targetdir "./Bin"

files
{
    "Src/PerfTest/*.cs",
}

defines
{
	
}

links
{
    "System",
    "System.Core",
    "IFix.Core",
}

configuration { "Debug*" }
    defines { "DEBUG" }
    symbols "On"
    clr "Unsafe"
 
configuration { "Release*" }
    flags   { "Optimize" }
    clr "Unsafe"