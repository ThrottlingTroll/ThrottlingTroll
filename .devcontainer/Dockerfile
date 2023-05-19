# Find the Dockerfile at this URL
# https://github.com/Azure/azure-functions-docker/blob/dev/host/4/bullseye/amd64/dotnet/dotnet-isolated/dotnet-isolated-core-tools.Dockerfile
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated6.0-core-tools

RUN wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
     && sudo dpkg -i packages-microsoft-prod.deb \
     && rm packages-microsoft-prod.deb \
     && sudo apt-get update \
     && sudo apt-get install -y dotnet-sdk-7.0
     