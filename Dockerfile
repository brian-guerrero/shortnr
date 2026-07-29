# ── build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore NuGet packages
COPY src/Shortnr.Data/Shortnr.Data.csproj  Shortnr.Data/
COPY src/Shortnr.Web/Shortnr.Web.csproj    Shortnr.Web/
RUN dotnet restore Shortnr.Web/Shortnr.Web.csproj

# install libman and restore frontend assets
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli
ENV PATH="$PATH:/root/.dotnet/tools"
COPY src/Shortnr.Web/libman.json Shortnr.Web/
RUN cd Shortnr.Web && libman restore

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
