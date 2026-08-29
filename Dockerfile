# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY MentalNoteMcp.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS final
WORKDIR /app

COPY --from=build /app .

ENV MENTAL_NOTES_DB_PATH=/data/mental-notes.db

RUN mkdir -p /data \
    && chown -R app:app /app /data

USER app

VOLUME /data

ENTRYPOINT ["dotnet", "MentalNoteMcp.dll"]
