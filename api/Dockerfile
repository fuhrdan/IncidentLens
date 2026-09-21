# The build context is the repository root: docker build -f api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api/IncidentLens.Api.csproj api/
RUN dotnet restore api/IncidentLens.Api.csproj
COPY api/ api/
RUN dotnet publish api/IncidentLens.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false \
    && rm -f /out/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install --no-install-recommends -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:8080 DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /out/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "IncidentLens.Api.dll"]
