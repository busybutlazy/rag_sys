FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY BeServer.sln .
COPY BeServer/BeServer.csproj BeServer/
RUN dotnet restore BeServer/BeServer.csproj
COPY . .
RUN dotnet publish BeServer/BeServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8001
ENTRYPOINT ["dotnet", "BeServer.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY BeServer.sln .
COPY BeServer/BeServer.csproj BeServer/
RUN dotnet restore BeServer/BeServer.csproj
EXPOSE 8001
CMD ["dotnet", "watch", "run", "--project", "BeServer/BeServer.csproj", "--urls", "http://+:8001"]
