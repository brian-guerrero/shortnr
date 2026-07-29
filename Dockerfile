# ── build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore
COPY src/Shortnr.Data/Shortnr.Data.csproj  Shortnr.Data/
COPY src/Shortnr.Web/Shortnr.Web.csproj    Shortnr.Web/
RUN dotnet restore Shortnr.Web/Shortnr.Web.csproj

# download and bundle frontend assets
RUN apt-get update -qq && apt-get install -y --no-install-recommends curl \
 && mkdir -p Shortnr.Web/wwwroot/lib \
 && curl -fsSL https://cdn.jsdelivr.net/npm/@picocss/pico@2/css/pico.min.css \
         -o Shortnr.Web/wwwroot/lib/pico.min.css \
 && curl -fsSL https://unpkg.com/htmx.org@2/dist/htmx.min.js \
         -o Shortnr.Web/wwwroot/lib/htmx.min.js \
 && curl -fsSL https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js \
         -o Shortnr.Web/wwwroot/lib/chart.umd.min.js \
 && curl -fsSL https://cdn.jsdelivr.net/npm/alpinejs@3/dist/cdn.min.js \
         -o Shortnr.Web/wwwroot/lib/alpine.min.js \
 && apt-get purge -y curl && rm -rf /var/lib/apt/lists/*

# copy source and publish
COPY src/Shortnr.Data/ Shortnr.Data/
COPY src/Shortnr.Web/  Shortnr.Web/
RUN dotnet publish Shortnr.Web/Shortnr.Web.csproj \
    -c Release -o /app/publish --no-restore

# ── runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir /data
VOLUME /data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__DefaultConnection="Data Source=/data/shortnr.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "Shortnr.Web.dll"]
