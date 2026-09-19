# syntax=docker/dockerfile:1
#
# Build context is the repository root (see docker-compose.yml), because a .NET solution build
# needs the shared Directory.*.props files and every referenced project, not just this folder.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only what restore needs first. This layer is then cached across every source-only change,
# which is the difference between a 15-second rebuild and a 3-minute one.
# .editorconfig carries the analyzer configuration, including the exemption for EF-generated
# migrations. Without it the build inside the container is stricter than the one outside it.
COPY Directory.Build.props Directory.Packages.props .editorconfig nuget.config* ./
COPY src/Comments.Domain/Comments.Domain.csproj src/Comments.Domain/
COPY src/Comments.Application/Comments.Application.csproj src/Comments.Application/
COPY src/Comments.Infrastructure/Comments.Infrastructure.csproj src/Comments.Infrastructure/
COPY src/Comments.Api/Comments.Api.csproj src/Comments.Api/
RUN dotnet restore src/Comments.Api/Comments.Api.csproj

COPY src/ src/
RUN dotnet publish src/Comments.Api/Comments.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS); setting ASPNETCORE_URLS as well
# only makes Kestrel warn that one overrides the other.
ENV DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1

# curl is not in the base image, and compose needs it to gate dependent services on this one being
# actually ready rather than merely started.
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Runs as the image's non-root "app" user: a container that does not need root should not have it.
USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=10 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Threadline.Comments.Api.dll"]
