FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

COPY global.json ./
COPY src/WebWritingTool.Domain/WebWritingTool.Domain.csproj src/WebWritingTool.Domain/
COPY src/WebWritingTool.Application/WebWritingTool.Application.csproj src/WebWritingTool.Application/
COPY src/WebWritingTool.Infrastructure/WebWritingTool.Infrastructure.csproj src/WebWritingTool.Infrastructure/
COPY src/WebWritingTool.Web/WebWritingTool.Web.csproj src/WebWritingTool.Web/

RUN dotnet restore src/WebWritingTool.Web/WebWritingTool.Web.csproj

COPY . .

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
