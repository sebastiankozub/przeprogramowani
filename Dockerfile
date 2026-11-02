# syntax=docker/dockerfile:1

# ---- Stage 1: build Angular frontend ----
FROM node:22 AS client-build
WORKDIR /client
COPY ClientApp/package*.json ./
RUN npm ci
COPY ClientApp/ ./
RUN npm run build -- --configuration=production

# ---- Stage 2: build & publish .NET backend ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY movie-date.csproj ./
RUN dotnet restore movie-date.csproj
COPY . .
COPY --from=client-build /client/dist/client-app/browser ./wwwroot
RUN dotnet publish movie-date.csproj -c Release -o /app/publish --no-restore

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend-build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "movie-date.dll"]
