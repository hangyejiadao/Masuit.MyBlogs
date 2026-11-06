FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Masuit.ReverseTunnel.Client.csproj .
RUN dotnet restore Masuit.ReverseTunnel.Client.csproj
COPY . .
RUN dotnet publish Masuit.ReverseTunnel.Client.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ReverseTunnelClient__DataDirectory=/data
EXPOSE 8080
VOLUME ["/data"]
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Masuit.ReverseTunnel.Client.dll"]
