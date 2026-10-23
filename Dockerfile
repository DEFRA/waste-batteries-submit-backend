# Build stage image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
WORKDIR "/src"
# POM integration tests start MongoDB through Testcontainers. A docker build has
# no Docker daemon, so those tests cannot run here. The pull request workflow
# runs the full suite with scripts/run-tests.sh, where Docker is available.
RUN dotnet build WasteBatteriesSubmitBackend.slnx -c Debug \
 && dotnet tests/WasteBatteriesSubmitBackend.Tests.Unit/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Unit.dll \
 && dotnet tests/WasteBatteriesSubmitBackend.Tests.Integration/bin/Debug/net10.0/WasteBatteriesSubmitBackend.Tests.Integration.dll \
      -namespace- WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission
RUN dotnet publish src/WasteBatteriesSubmitBackend -c Release -o /app/publish /p:UseAppHost=false

# Final production image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

USER root
# Add curl to template, CDP PLATFORM HEALTHCHECK REQUIREMENT
RUN apt update && \
    apt install curl -y && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8085
ENTRYPOINT ["dotnet", "WasteBatteriesSubmitBackend.dll"]
