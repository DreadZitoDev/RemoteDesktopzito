@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM  CONFIGURACION - edita solo estas lineas
REM ============================================================
set "EXE=RemoteDesk.exe"
set "CARPETA=%~dp0"
set "PUERTO=5900"
set "PASSWORD="
REM ============================================================

title Host - Escuchando en el puerto %PUERTO%

cd /d "%CARPETA%"

if not exist "%EXE%" (
    echo.
    echo  [ERROR] No se encontro "%EXE%" en:
    echo          %CARPETA%
    echo.
    echo  Edita la variable EXE o CARPETA al principio de este .bat
    echo.
    pause
    exit /b 1
)

echo.
echo  ===========================================
echo    MODO HOST  -  esta PC sera controlada
echo  ===========================================
echo.
echo  Puerto: %PUERTO%
echo.
echo  IPs de esta maquina (pasale una al cliente):
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do echo    %%a
echo.
echo  Cerra esta ventana para detener el host.
echo  -------------------------------------------
echo.

if defined PASSWORD (
    "%EXE%" host --port %PUERTO% --password "%PASSWORD%"
) else (
    "%EXE%" host --port %PUERTO%
)

set "CODIGO=%errorlevel%"
echo.
if not "%CODIGO%"=="0" (
    echo  [!] El host termino con codigo de error %CODIGO%
) else (
    echo  Host detenido.
)
echo.
pause
endlocal
