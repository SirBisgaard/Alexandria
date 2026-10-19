FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Alexandria.Api/Alexandria.Api.csproj", "Alexandria.Api/"]
COPY ["Alexandria.Business/Alexandria.Business.csproj", "Alexandria.Business/"]
COPY ["Alexandria.DataAccess/Alexandria.DataAccess.csproj", "Alexandria.DataAccess/"]
RUN dotnet restore "Alexandria.Api/Alexandria.Api.csproj"
COPY . .
WORKDIR "/src/Alexandria.Api"
RUN dotnet build "./Alexandria.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=
RUN dotnet publish "./Alexandria.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false ${VERSION:+/p:InformationalVersion=$VERSION}

FROM base AS final
WORKDIR /app
COPY --from=mwader/static-ffmpeg:latest /ffmpeg /ffprobe /usr/local/bin/
COPY --from=publish /app/publish .
RUN chmod +x /app/kepubify
USER $APP_UID
ENTRYPOINT ["dotnet", "Alexandria.Api.dll"]
