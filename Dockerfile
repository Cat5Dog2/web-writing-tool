FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Directory.Build.props carries RestorePackagesWithLockFile, and the lock files are what
# --locked-mode below reads. Both are copied with the project files so the restore layer stays
# cacheable.
COPY global.json Directory.Build.props ./
COPY src/WebWritingTool.Domain/WebWritingTool.Domain.csproj src/WebWritingTool.Domain/packages.lock.json src/WebWritingTool.Domain/
COPY src/WebWritingTool.Application/WebWritingTool.Application.csproj src/WebWritingTool.Application/packages.lock.json src/WebWritingTool.Application/
COPY src/WebWritingTool.Infrastructure/WebWritingTool.Infrastructure.csproj src/WebWritingTool.Infrastructure/packages.lock.json src/WebWritingTool.Infrastructure/
COPY src/WebWritingTool.Web/WebWritingTool.Web.csproj src/WebWritingTool.Web/packages.lock.json src/WebWritingTool.Web/

# Warms the package cache in a layer that only changes when a csproj or a lock file changes. Not
# locked, deliberately: with only the project files present the SDK does not add the implicit
# static web asset reference it adds once the Razor content is there, so the reference set here
# does not match the lock files and locked mode would reject it (NU1004).
RUN dotnet restore src/WebWritingTool.Web/WebWritingTool.Web.csproj

COPY . .

# The enforcing restore, with the full source present so the reference set matches what generated
# the lock files. --locked-mode refuses to resolve anything they do not already name, so the same
# commit cannot produce an image with different dependencies on a different day. Cheap despite
# being a second restore: every package is already in the image's NuGet cache from the layer above.
RUN dotnet restore src/WebWritingTool.Web/WebWritingTool.Web.csproj --locked-mode

RUN dotnet publish src/WebWritingTool.Web/WebWritingTool.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

EXPOSE 8080

RUN mkdir -p /var/app/keys /var/app/storage \
    && chown -R app:app /var/app

USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "WebWritingTool.Web.dll"]
