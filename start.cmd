@echo off
rem Starts the whole Keke Beauty stack (PostgreSQL/PostGIS first, then storage, API and website) through Docker.
rem   start.cmd          starts what already exists (fast)
rem   start.cmd build    rebuilds the API and the website first (after code changes)
setlocal
cd /d "%~dp0"

docker info >nul 2>&1
if errorlevel 1 (
  echo Docker Desktop n'est pas demarre. Lancez-le, attendez qu'il soit pret, puis relancez ce script.
  exit /b 1
)

if not exist .env (
  echo Fichier .env absent : generez les secrets d'abord avec "sh scripts/init-env.sh" ^(Git Bash ou WSL^), puis relancez ce script.
  exit /b 1
)

set BUILD=
if /i "%~1"=="build" set BUILD=--build

echo Demarrage de Keke Beauty : base de donnees, stockage, API et site...
docker compose up -d %BUILD%
if errorlevel 1 (
  echo Echec du demarrage : voir les messages ci-dessus.
  exit /b 1
)

echo Attente de l'API (migrations et donnees de depart au premier lancement)...
powershell -NoProfile -Command "for($i=0;$i -lt 60;$i++){ try { if((Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/ready).StatusCode -eq 200){ exit 0 } } catch {}; Start-Sleep -Seconds 3 }; exit 1"
if errorlevel 1 (
  echo L'API ne repond pas encore. Consultez : docker compose logs api
  exit /b 1
)

echo.
echo Keke Beauty est pret :
echo   Site            http://localhost:4200
echo   API (OpenAPI)   http://localhost:8080/openapi/v1.json
echo   E-mails de test http://localhost:8025
echo   Console MinIO   http://localhost:9001
echo   PostgreSQL      localhost:5434  (base kekebeauty, utilisateur keke)
echo.
echo Les services redemarrent seuls avec Docker. Pour les arreter : stop.cmd
endlocal
