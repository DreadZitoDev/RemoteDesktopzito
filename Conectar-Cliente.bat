@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM  CONFIGURACION - edita solo estas lineas
REM ============================================================
set "EXE=RemoteDesk.exe"
set "CARPETA=%~dp0"
set "PUERTO=5900"
set "IP_FIJA="
set "PASSWORD="
REM  Si dejas IP_FIJA vacia, el .bat te la pregunta cada vez.
REM  Ejemplo:  set "IP_FIJA=192.168.1.45"
REM ============================================================

title Cliente - Conectar a host remoto

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
echo    MODO CLIENTE  -  vos controlas la otra PC
echo  ===========================================
echo.

if defined IP_FIJA (
    set "IP=%IP_FIJA%"
    echo  Conectando a la IP configurada: %IP_FIJA%
) else (
    set /p "IP=  IP del host: "
)

if not defined IP (
    echo.
    echo  [ERROR] No ingresaste ninguna IP.
    echo.
    pause
    exit /b 1
)

echo.
echo  Conectando a %IP%:%PUERTO% ...
echo  -------------------------------------------
echo.

if defined PASSWORD (
    "%EXE%" client --host %IP% --port %PUERTO% --password "%PASSWORD%"
) else (
    "%EXE%" client --host %IP% --port %PUERTO%
)

set "CODIGO=%errorlevel%"
echo.
if not "%CODIGO%"=="0" (
    echo  [!] La conexion fallo o se corto (codigo %CODIGO%).
    echo      Revisa que el host este corriendo y que el firewall
    echo      permita el puerto %PUERTO%.
) else (
    echo  Sesion finalizada.
)
echo.
pause
endlocal
