FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project or package version changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/JobHunt.Api/JobHunt.Api.csproj src/JobHunt.Api/packages.lock.json src/JobHunt.Api/
RUN dotnet restore src/JobHunt.Api --locked-mode

COPY src/ src/
RUN dotnet publish src/JobHunt.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN mkdir /data && chown $APP_UID /data
USER $APP_UID

COPY --from=build /app .

ENV ConnectionStrings__Default="Data Source=/data/jobhunt.db"
VOLUME /data
EXPOSE 8080

ENTRYPOINT ["dotnet", "JobHunt.Api.dll"]
