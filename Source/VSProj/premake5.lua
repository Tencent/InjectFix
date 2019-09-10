
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
    "Src/TestDLL/BaseTest.cs",
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

project "IFix.TestDLL.Redirect"
language "C#"
kind "SharedLib"
framework "3.5"
targetdir "./Lib"

files
{
    "Src/TestDLL/RedirectBaseTest.cs",
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

project "IFix.UnitTest"
language "C#"
kind "SharedLib"
framework "3.5"
targetdir "./Lib"

dependson { "IFix.Core", "IFix.TestDLL", "IFix.TestDLL.Redirect", "IFix" }

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
        "$(SolutionDir)/inject_redirect_dll",
    }
 
configuration { "Release*" }
    flags   { "Optimize" }
    clr "Unsafe"
    prebuildcommands
    { 
        "$(SolutionDir)/inject_redirect_dll",
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