:: delete all files in the "build" folder that could exist from last time
DEL /F /S /Q build 1>nul
RMDIR /S /Q build

:: pull down my git repo, inside that repo is a script that will build itself, create docker image and run image in a local docker container
:: it assumes docker is running on the environment you are in.
:: TODO - Should just put the release artifacts not the entire repo for this
git clone --branch main https://gitlab.com/nyetools/changelogtracker.git build

:: Alongside this script is a dependency for my bot, a config file the bot needs to grab when it runs, copy that in. 
echo f | xcopy ".\config.json" ".\build\config.json"

:: Launch docker-compose inside build folder 
cd ./build
call docker-compose up -d --build

::set /p DUMMY=Hit ENTER to continue...