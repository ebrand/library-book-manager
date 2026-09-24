# plt-svc-001 REQ-SVC-003. NONCONFORMING PLACEHOLDER: the platform base-image registry and
# its supported tags are not named anywhere available to this build (QUESTIONS.md Q-STD-03).
# Both FROM lines must be replaced with platform-registry images before this ships.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props LibraryBookManager.sln ./
COPY src/LibraryBookManager.Api/LibraryBookManager.Api.csproj src/LibraryBookManager.Api/packages.lock.json src/LibraryBookManager.Api/
# REQ-SVC-004: restore only what the committed lock file names.
RUN dotnet restore src/LibraryBookManager.Api/LibraryBookManager.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/LibraryBookManager.Api/LibraryBookManager.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# The application runs as the image's non-root user, which cannot write /app; the database lives on /data.
RUN mkdir /data && chown $APP_UID /data
VOLUME /data
ENV ConnectionStrings__Library="Data Source=/data/library.db"
USER $APP_UID
ENTRYPOINT ["dotnet", "LibraryBookManager.Api.dll"]
