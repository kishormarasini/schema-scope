FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json .
COPY src/SchemaScope/SchemaScope.csproj src/SchemaScope/
RUN dotnet restore src/SchemaScope
COPY src/ src/
RUN dotnet publish src/SchemaScope -c Release -o /app --no-restore --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0
COPY --from=build /app /app
ENTRYPOINT ["dotnet", "/app/schemascope.dll"]
