# Pinned by digest, not by the 10.0 tag. The Web SDK adds Microsoft.AspNetCore.App.Internal.Assets
# implicitly, at the ASP.NET Core version the SDK carries, so a moved tag changes a direct package
# reference and the locked restore below rejects the tracked lock files with NU1004. The SDK and the
# lock files have to move in the same commit.
#
# The matching tag is 10.0 (SDK 10.0.401, ASP.NET Core 10.0.12); global.json pins the same SDK for
# host runs. To move it, follow docs/ci-cd-design.md "SDKイメージのdigest更新".
FROM mcr.microsoft.com/dotnet/sdk@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5 AS build

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

# Pinned by digest for the same reason as the build stage: what the application runs on is decided in
# review, not by whichever image the tag points at during a build. The trade-off is that upstream
# runtime patches now arrive only when this line is bumped, so the image scan failing on the pinned
# base is the signal to bump it rather than something to work around.
#
# The matching tag is 10.0 (ASP.NET Core 10.0.12).
FROM mcr.microsoft.com/dotnet/aspnet@sha256:1fe86375600b62e6566b465da9553eef0621f13c67f40fe764cd8dbb1dee1497 AS runtime

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

EXPOSE 8080

RUN mkdir -p /var/app/keys /var/app/storage \
    && chown -R app:app /var/app

USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "WebWritingTool.Web.dll"]
