FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

RUN curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb \
     && dpkg -i /tmp/packages-microsoft-prod.deb \
     && rm /tmp/packages-microsoft-prod.deb \
     && apt-get update \
     && apt-get install -y azure-functions-core-tools-4