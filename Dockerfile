# ── build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore NuGet packages (including Microsoft.Web.LibraryManager.Build)
COPY src/Shortnr.Data/Shortnr.Data.csproj          Shortnr.Data/
COPY src/Shortnr.ServiceDefaults/Shortnr.ServiceDefaults.csproj Shortnr.ServiceDefaults/
COPY src/Shortnr.Web/Shortnr.Web.csproj            Shortnr.Web/
RUN dotnet restore Shortnr.Web/Shortnr.Web.csproj

# copy source and publish
# Microsoft.Web.LibraryManager.Build runs libman restore automatically during publish
COPY src/Shortnr.Data/            Shortnr.Data/
COPY src/Shortnr.ServiceDefaults/ Shortnr.ServiceDefaults/
COPY src/Shortnr.Web/             Shortnr.Web/
RUN dotnet publish Shortnr.Web/Shortnr.Web.csproj \
    -c Release -o /app/publish

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
