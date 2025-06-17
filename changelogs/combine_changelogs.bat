@echo off
setlocal EnableDelayedExpansion
rem This file combines all the other .txt files in this directory into changelog.txt,
rem or another file name given as optional argument.

rem Determine script's absolute directory and use that for paths
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "DEFAULT_TARGET_FILE=changelog.txt"

rem A different target file can be provided as argument
if "%~1"=="" (
    set "TARGET_FILE=%SCRIPT_DIR%\%DEFAULT_TARGET_FILE%"
) else (
    set "TARGET_FILE=%SCRIPT_DIR%\%~1"
)

if not exist "%TARGET_FILE%" (
    echo %TARGET_FILE% doesn't exist yet, creating it
    type nul > "%TARGET_FILE%"
)

echo.
echo Appending *.txt into %TARGET_FILE%:
for %%F in ("%SCRIPT_DIR%\*.txt") do (
    if exist "%%F" (
        rem Skip target file(s) that we're appending into
        set "BASENAME=%%~nxF"
        if /i "!BASENAME!"=="%DEFAULT_TARGET_FILE%" (
            echo  - Skipping target file: %%~nxF
        ) else (
            for %%T in ("%TARGET_FILE%") do (
                if /i "!BASENAME!"=="%%~nxT" (
                    echo  - Skipping target file: %%~nxF
                ) else (
                    echo  + %%~nxF
                    rem Strip path and extension from the file name and append as heading
                    echo %%~nF>> "%TARGET_FILE%"
                    rem Append file content
                    type "%%F" >> "%TARGET_FILE%"
                    rem Add a newline after each file's content
                    echo.>> "%TARGET_FILE%"
                )
            )
        )
    ) else (
        echo ERROR: %%F is missing or not a proper file, skipping!
    )
)

echo.
rem Finished file name in upper case and without path as heading
for %%T in ("%TARGET_FILE%") do echo === %%~nxT:
echo.
rem Print the finished file itself
type "%TARGET_FILE%"
