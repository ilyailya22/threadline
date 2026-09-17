# syntax=docker/dockerfile:1
#
# Build context is the repository root — see the note in src/Comments.Api/Dockerfile.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config* ./
COPY src/Comments.Domain/Comments.Domain.csproj src/Comments.Domain/
COPY src/Comments.Application/Comments.Application.csproj src/Comments.Application/
COPY src/Comments.Infrastructure/Comments.Infrastructure.csproj src/Comments.Infrastructure/
COPY src/Comments.Worker/Comments.Worker.csproj src/Comments.Worker/
RUN dotnet restore src/Comments.Worker/Comments.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Comments.Worker/Comments.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

ENV DOTNET_gcServer=1

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "Threadline.Comments.Worker.dll"]
