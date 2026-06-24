@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformX.Application\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal

@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformX.Domain\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal

@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformXUI\PlatformXUI.Controls\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal

@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformXUI\PlatformXUI.Controls.Resources\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal

@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformXUI\PlatformXUI.Services\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal


@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformXUI\PlatformXUI.Shell\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal


@echo off
setlocal enabledelayedexpansion

rem 指定 nupkg 文件所在目录
set "directory=D:\SuMi\PlatformX\PlatformX.Infrastructure\bin\Release"

rem 指定 NuGet 源 URL 和 API 密钥
set "nugetSource=https://sumi.freqman.work:18451/v3/index.json"
set "apiKey=sumi123456"

rem 初始化变量
set "latestFile="
set "latestFileTime="

rem 查找最新修改的 nupkg 文件
for %%f in (%directory%\*.nupkg) do (
    rem 获取文件修改时间
    for %%t in ("%%f") do set "fileTime=%%~tf"

    if not defined latestFileTime (
        set "latestFile=%%f"
        set "latestFileTime=!fileTime!"
    ) else (
        rem 比较文件修改时间
        if "!fileTime!" gtr "!latestFileTime!" (
            set "latestFile=%%f"
            set "latestFileTime=!fileTime!"
        )
    )
)

if defined latestFile (
    echo Pushing latest package: !latestFile!
    dotnet nuget push "!latestFile!" -s %nugetSource% -k %apiKey%
) else (
    echo No nupkg files found in %directory%
)

endlocal


pause


