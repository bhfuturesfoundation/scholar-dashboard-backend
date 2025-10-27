# ---------------------------
# STAGE 1: Build
# ---------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY Auth.API/Auth.API.csproj Auth.API/
COPY Auth.Models/Auth.Models.csproj Auth.Models/
COPY Auth.Services/Auth.Services.csproj Auth.Services/

# Restore dependencies
RUN dotnet restore

# Copy the rest of the code
COPY . .

# Publish API project
WORKDIR /src/Auth.API
RUN dotnet publish -c Release -o /app/publish

# ---------------------------
# STAGE 2: Runtime
# ---------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway expects port 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Auth.API.dll"]
