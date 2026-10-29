FROM mcr.microsoft.com/dotnet/sdk:11.0-preview-alpine AS build
ARG RID=linux-musl-x64
WORKDIR /src

COPY Directory.Packages.props .
COPY src/HouseholdApp.Application/HouseholdApp.Application.csproj src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/HouseholdApp.ServiceDefaults.csproj src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/HouseholdApp.Web.csproj src/HouseholdApp.Web/
COPY src/HouseholdApp.Migrations/HouseholdApp.Migrations.csproj src/HouseholdApp.Migrations/
RUN dotnet restore src/HouseholdApp.Web/HouseholdApp.Web.csproj -r $RID -p:PublishReadyToRun=true

COPY src/HouseholdApp.Application/ src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/ src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/ src/HouseholdApp.Web/
COPY src/HouseholdApp.Migrations/ src/HouseholdApp.Migrations/
RUN dotnet publish src/HouseholdApp.Web/HouseholdApp.Web.csproj -c Release -o /app/publish --no-restore -r $RID --self-contained false -p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview-alpine AS final
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HouseholdApp.Web.dll"]
