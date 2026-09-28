FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FalazaLodge.Api.csproj", "./"]
RUN dotnet restore "FalazaLodge.Api.csproj"
COPY . .
RUN dotnet publish "FalazaLodge.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FalazaLodge.Api.dll"]
