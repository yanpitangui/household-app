FROM mcr.microsoft.com/dotnet/sdk:11.0-preview-alpine AS build
ARG RID=linux-musl-x64
WORKDIR /src

COPY Directory.Packages.props .
COPY src/HouseholdApp.Application/HouseholdApp.Application.csproj src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/HouseholdApp.ServiceDefaults.csproj src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/HouseholdApp.Web.csproj src/HouseholdApp.Web/
COPY src/HouseholdApp.Migrations/HouseholdApp.Migrations.csproj src/HouseholdApp.Migrations/
COPY src/HouseholdApp.Migrator/HouseholdApp.Migrator.csproj src/HouseholdApp.Migrator/
RUN dotnet restore src/HouseholdApp.Web/HouseholdApp.Web.csproj -r $RID -p:PublishReadyToRun=true && \
    dotnet restore src/HouseholdApp.Migrator/HouseholdApp.Migrator.csproj -r $RID -p:PublishReadyToRun=true

COPY src/HouseholdApp.Application/ src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/ src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/ src/HouseholdApp.Web/
COPY src/HouseholdApp.Migrations/ src/HouseholdApp.Migrations/
COPY src/HouseholdApp.Migrator/ src/HouseholdApp.Migrator/
RUN dotnet publish src/HouseholdApp.Web/HouseholdApp.Web.csproj -c Release -o /app/publish --no-restore -r $RID --self-contained false -p:PublishReadyToRun=true && \
    dotnet publish src/HouseholdApp.Migrator/HouseholdApp.Migrator.csproj -c Release -o /app/migrator --no-restore -r $RID --self-contained false -p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview-alpine AS final
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/migrator /app/migrator
ENTRYPOINT ["dotnet", "HouseholdApp.Web.dll"]
