@echo off
rem Stops the Keke Beauty stack without deleting anything: the database, the uploaded images and the accounts are kept.
setlocal
cd /d "%~dp0"
docker compose stop
echo Keke Beauty est arrete. Les donnees sont conservees ; relancez start.cmd pour reprendre.
endlocal
