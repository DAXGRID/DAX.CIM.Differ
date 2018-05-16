@echo off

set version=%1
set currentdir=%~dp0
set root=%currentdir%

set destination=%root%\deploy
set msbuild=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe
set nuget=%root%\tools\NuGet\nuget.exe

set solutionfile=%root%\DAX.CIM.Differ.sln
set nuspec=%currentdir%\DAX.CIM.Differ\DAX.CIM.Differ.nuspec

if "%version%"=="" (
	echo Please remember to specify which version to build as an argument to this script.
	echo.
	goto exit
)


"%msbuild%" "%solutionfile%" /p:Configuration=Release

"%nuget%" pack "%nuspec%" -OutputDirectory "%destination%" -Version "%version%"


:exit