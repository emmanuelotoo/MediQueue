# Build the whole solution, then ship only the published output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so a code change does not invalidate the restore layer.
COPY Directory.Build.props MediQueue.slnx ./
COPY src/MediQueue.Domain/*.csproj src/MediQueue.Domain/
COPY src/MediQueue.Shared/*.csproj src/MediQueue.Shared/
COPY src/MediQueue.Infrastructure/*.csproj src/MediQueue.Infrastructure/
COPY src/MediQueue.Api/*.csproj src/MediQueue.Api/
COPY src/MediQueue.Client/*.csproj src/MediQueue.Client/
COPY tests/MediQueue.Domain.Tests/*.csproj tests/MediQueue.Domain.Tests/
COPY tests/MediQueue.Api.Tests/*.csproj tests/MediQueue.Api.Tests/
COPY tests/MediQueue.Client.Tests/*.csproj tests/MediQueue.Client.Tests/
RUN dotnet restore MediQueue.slnx

COPY . .
RUN dotnet publish src/MediQueue.Api/MediQueue.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Runs unprivileged: the image serves a public waiting-room kiosk.
RUN useradd --uid 5678 --create-home mediqueue && chown -R mediqueue /app
USER mediqueue

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MediQueue.Api.dll"]
