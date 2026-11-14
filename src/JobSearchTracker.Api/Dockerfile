# Build context is the repository root: docker build -f src/JobSearchTracker.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/JobSearchTracker.Api/JobSearchTracker.Api.csproj src/JobSearchTracker.Api/
RUN dotnet restore src/JobSearchTracker.Api/JobSearchTracker.Api.csproj
COPY src/JobSearchTracker.Api/ src/JobSearchTracker.Api/
RUN dotnet publish src/JobSearchTracker.Api/JobSearchTracker.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobSearchTracker.Api.dll"]
