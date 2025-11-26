@echo off
setlocal

:: The path to PostgreSQL's binaries is passed as the first argument
set "POSTGRESQL_BIN_PATH=%1"
:: Password for the postgres user is passed as the second argument
set "POSTGRES_PASSWORD=%2"
:: Password for the sinutronic user is passed as the third argument
set "SINUTRONIC_PASSWORD=%3"

:: Set the full path to your SQL file
set "CURRENT_DIR=%CD%"
set "SQL_FILE_NAME=database_init.sql"
set "FULL_SQL_PATH=%CURRENT_DIR%\%SQL_FILE_NAME%"

:: Check the OS type and set the appropriate path
if not "%OS%"=="Windows_NT" (
    echo Unsupported OS or PostgreSQL bin not found
    exit /b
)

:: Setting up environment for database commands
cd /d %POSTGRESQL_BIN_PATH%
set PGPASSWORD=%POSTGRES_PASSWORD%

:: Create the database user and give them the necessary permissions
psql -h localhost -p 5432 -U postgres -c "CREATE USER sinutronic WITH ENCRYPTED PASSWORD '%SINUTRONIC_PASSWORD%'; ALTER USER sinutronic WITH CREATEDB;"
if ERRORLEVEL 1 (
    echo Failed to create user or modify database permissions
    goto CleanUp
)

:: Create the database with the newly created user as owner
psql -h localhost -p 5432 -U postgres -c "CREATE DATABASE sinutronic WITH OWNER sinutronic;"
if ERRORLEVEL 1 (
    echo Failed to create database
    goto CleanUp
)

:: Run the SQL script for database initialization using 'sinutronic' password
set PGPASSWORD=%SINUTRONIC_PASSWORD%
psql -h localhost -p 5432 -U sinutronic -d sinutronic -f "%FULL_SQL_PATH%"
if ERRORLEVEL 1 (
    echo Failed to run SQL script
    goto CleanUp
)

:CleanUp
endlocal
exit /b


:: Cleanup and exit
:CleanUp
set PGPASSWORD=
endlocal
echo Script execution complete.
exit /b
