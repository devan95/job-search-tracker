# Build context is the repository root: docker build -f src/JobSearchTracker.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/JobSearchTracker.Worker/JobSearchTracker.Worker.csproj src/JobSearchTracker.Worker/
RUN dotnet restore src/JobSearchTracker.Worker/JobSearchTracker.Worker.csproj
COPY src/JobSearchTracker.Worker/ src/JobSearchTracker.Worker/
RUN dotnet publish src/JobSearchTracker.Worker/JobSearchTracker.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "JobSearchTracker.Worker.dll"]
