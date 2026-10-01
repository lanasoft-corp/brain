# Image de la passerelle pour la démo. En CI, l'image est construite par le SDK .NET
# (dotnet publish -t:PublishContainer), sans Dockerfile.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LanaSoft.Brain.Mcp/ ./
RUN dotnet publish LanaSoft.Brain.Mcp.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LanaSoft.Brain.Mcp.dll"]
