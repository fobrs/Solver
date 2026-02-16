
cd /volume1/homes/fo/projects/dotnet/Solver
# export Microsoft_CodeAnalysis_EditAndContinue_LogDir=./../logs
# export DOTNET_ROOT=/var/services/homes/fo/projects/dotnet/.dotnet
# ../.dotnet/dotnet run -c Release
dotnet watch --non-interactive --verbose --no-hot-reload run -c Release
