FROM mcr.microsoft.com/dotnet/sdk:11.0-preview-alpine AS build
WORKDIR /src

COPY Directory.Packages.props .
COPY src/HouseholdApp.Application/HouseholdApp.Application.csproj src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/HouseholdApp.ServiceDefaults.csproj src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/HouseholdApp.Web.csproj src/HouseholdApp.Web/
RUN dotnet restore src/HouseholdApp.Web/HouseholdApp.Web.csproj

COPY src/HouseholdApp.Application/ src/HouseholdApp.Application/
COPY src/HouseholdApp.ServiceDefaults/ src/HouseholdApp.ServiceDefaults/
COPY src/HouseholdApp.Web/ src/HouseholdApp.Web/
RUN dotnet publish src/HouseholdApp.Web/HouseholdApp.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview-alpine AS final
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HouseholdApp.Web.dll"]
